# Builds a classic Windows Setup.exe for Compressi (Inno Setup).
param(
    [ValidateSet('x64')]
    [string]$Platform = 'x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Optional semver (e.g. 1.0.10). Overrides Inno Setup + assembly version for this build.
    [string]$Version,

    # CI defaults favor wall-clock; local defaults favor smaller installers / faster app startup.
    [bool]$ReadyToRun,

    [ValidateSet('lzma2/max', 'lzma2', 'lzma2/fast', 'zip')]
    [string]$Compression,

    [bool]$SolidCompression
)

# CI and local both prioritize download size for auto-update.
if (-not $PSBoundParameters.ContainsKey('ReadyToRun')) {
    # ReadyToRun speeds cold start but inflates the installer; keep it off for release packs.
    $ReadyToRun = $false
}
if (-not $PSBoundParameters.ContainsKey('Compression')) {
    # Prefer smallest download size for auto-update (installer is mostly native binaries).
    $Compression = 'lzma2/max'
}
if (-not $PSBoundParameters.ContainsKey('SolidCompression')) {
    $SolidCompression = $true
}

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
$AppProject = Join-Path $Root 'Compressi.App\Compressi.App.csproj'
$IssFile = Join-Path $Root 'installer\Compressi.iss'
$OutputDir = Join-Path $Root 'installer\output'
$PublishDir = Join-Path $Root "Compressi.App\bin\$Configuration\net8.0-windows10.0.26100.0\win-$Platform\setup-publish"
$FfmpegDir = Join-Path $Root 'Compressi.App\Assets\ffmpeg'
$TotalSw = [System.Diagnostics.Stopwatch]::StartNew()

function Write-StepTime([string]$Label, [System.Diagnostics.Stopwatch]$Sw) {
    Write-Host ("[time] {0}: {1:N1}s" -f $Label, $Sw.Elapsed.TotalSeconds)
}

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

if (-not (Test-Path $AppProject)) {
    throw "App project not found: $AppProject"
}

if (-not (Test-Path $IssFile)) {
    throw "Inno Setup script not found: $IssFile"
}

$ffmpeg = Join-Path $FfmpegDir 'ffmpeg.exe'
$ffprobe = Join-Path $FfmpegDir 'ffprobe.exe'
$ffmpegDlls = @(Get-ChildItem -LiteralPath $FfmpegDir -Filter '*.dll' -File -ErrorAction SilentlyContinue)
if (-not (Test-Path $ffmpeg) -or -not (Test-Path $ffprobe) -or $ffmpegDlls.Count -eq 0) {
    throw "FFmpeg shared build missing under $FfmpegDir (need ffmpeg.exe, ffprobe.exe, and codec DLLs). Run scripts/get-ffmpeg.ps1."
}

$iscc = Find-Iscc
if (-not $iscc) {
    throw @"
Inno Setup 6 was not found.
Install it with:
  winget install --id JRSoftware.InnoSetup -e
Then re-run this script.
"@
}

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must look like major.minor.patch (got '$Version')."
    }
    Write-Host "Using version $Version"
}

Write-Host "Publishing Compressi ($Configuration, $Platform, unpackaged)..."
Write-Host "PublishReadyToRun=$ReadyToRun Compression=$Compression SolidCompression=$SolidCompression"
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

# Do not rely on *.pubxml (gitignored historically); pass publish settings explicitly for CI.
$publishArgs = @(
    $AppProject
    '-c', $Configuration
    '-r', "win-$Platform"
    "-p:Platform=$Platform"
    "-p:PublishDir=$PublishDir"
    '-p:WindowsPackageType=None'
    '-p:WindowsAppSDKSelfContained=true'
    '-p:SelfContained=true'
    '-p:PublishSingleFile=false'
    "-p:PublishReadyToRun=$ReadyToRun"
    '-p:PublishTrimmed=false'
    '-p:GenerateAppxPackageOnBuild=false'
    '-p:RunAnalyzers=false'
    '-p:RunAnalyzersDuringBuild=false'
    '-p:EnableNuGetAudit=false'
)
if ($Version) {
    $publishArgs += "-p:Version=$Version"
    $publishArgs += "-p:AssemblyVersion=$Version.0"
    $publishArgs += "-p:FileVersion=$Version.0"
    $publishArgs += "-p:InformationalVersion=$Version"
}

$publishSw = [System.Diagnostics.Stopwatch]::StartNew()
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
Write-StepTime 'dotnet publish' $publishSw

$appExe = Join-Path $PublishDir 'Compressi.App.exe'
if (-not (Test-Path $appExe)) {
    throw "Published app not found: $appExe"
}

# Optional surfaces that can still land in self-contained output even with component packages.
# Do NOT strip Microsoft.UI.Xaml.Phone.dll — WinUI initializes it at startup.
$unusedPublishPatterns = @(
    # AI / ML
    'onnxruntime.dll'
    'DirectML.dll'
    'Microsoft.ML.OnnxRuntime.dll'
    'Microsoft.Windows.AI*'
    'Microsoft.Windows.Internal.AI*'
    # Widgets
    'Microsoft.Windows.Widgets*'
    # WebView2 (WinUI dependency; Compressi never hosts a web view)
    'Microsoft.Web.WebView2*'
    'WebView2Loader.dll'
    # Phone control satellite MUIs only (keep Microsoft.UI.Xaml.Phone.dll)
    'Microsoft.UI.Xaml.Phone.dll.mui'
    # Debug / symbol helpers not needed at runtime
    'Microsoft.DiaSymReader*'
    'mscordaccore*'
    'mscordbi.dll'
    # Unused BCL / projections
    'Microsoft.VisualBasic*'
    'Microsoft.Security.Authentication.OAuth*'
    'System.Net.Mail.dll'
    # Win2D leftovers if a transitive package reintroduces them
    'Microsoft.Graphics.Canvas*'
)
$removedBytes = [long]0
foreach ($pattern in $unusedPublishPatterns) {
    Get-ChildItem -LiteralPath $PublishDir -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue |
        ForEach-Object {
            $removedBytes += $_.Length
            Remove-Item -LiteralPath $_.FullName -Force
        }
}

# Drop leftover empty locale folders that only held Phone.mui (or are now empty).
Get-ChildItem -LiteralPath $PublishDir -Directory -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -match '^[a-z]{2}([_-][a-zA-Z0-9]+)*$' -and
        $_.Name -notin @('en', 'en-us', 'en-US', 'en-GB') -and
        -not (Get-ChildItem -LiteralPath $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1)
    } |
    ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }

if ($removedBytes -gt 0) {
    Write-Host ("Stripped unused WinUI/.NET publish files: {0:N1} MB" -f ($removedBytes / 1MB))
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$setupIcon = Join-Path $Root 'Compressi.App\Assets\AppIcon.ico'
$solidValue = if ($SolidCompression) { 'yes' } else { 'no' }
$isccArgs = @(
    "/DPublishDir=$PublishDir"
    "/DOutputDir=$OutputDir"
    "/DCompression=$Compression"
    "/DSolidCompression=$solidValue"
)
if ($Version) {
    $isccArgs += "/DMyAppVersion=$Version"
}
if (Test-Path $setupIcon) {
    $isccArgs += "/DSetupIcon=$setupIcon"
}

Write-Host "Compiling installer with Inno Setup..."
$isccSw = [System.Diagnostics.Stopwatch]::StartNew()
& $iscc @isccArgs $IssFile
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}
Write-StepTime 'Inno Setup' $isccSw

$setupExe = Get-ChildItem $OutputDir -Filter 'Compressi-Setup-*-x64.exe' |
    Where-Object { $_.Name -match '^Compressi-Setup-\d+\.\d+\.\d+-x64\.exe$' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $setupExe) {
    throw "Setup.exe was not produced in $OutputDir"
}

# Stable name for GitHub releases/latest/download (versioned name kept for archives).
$stableSetup = Join-Path $OutputDir 'Compressi-Setup-x64.exe'
Copy-Item -LiteralPath $setupExe.FullName -Destination $stableSetup -Force

Write-StepTime 'total build-installer' $TotalSw
Write-Host ""
Write-Host "Installer ready:"
Write-Host "  $($setupExe.FullName)"
Write-Host "  $stableSetup"
Write-Host "  Size: $([math]::Round($setupExe.Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "Double-click the Setup.exe to install Compressi."
