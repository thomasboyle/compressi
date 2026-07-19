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

$isCI = $env:CI -eq 'true'
if (-not $PSBoundParameters.ContainsKey('ReadyToRun')) {
    $ReadyToRun = -not $isCI
}
if (-not $PSBoundParameters.ContainsKey('Compression')) {
    # zip is much faster for CI; local builds keep max compression for smaller downloads.
    $Compression = if ($isCI) { 'zip' } else { 'lzma2/max' }
}
if (-not $PSBoundParameters.ContainsKey('SolidCompression')) {
    $SolidCompression = -not $isCI
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
if (-not (Test-Path $ffmpeg) -or -not (Test-Path $ffprobe)) {
    throw "FFmpeg binaries missing under $FfmpegDir (need ffmpeg.exe and ffprobe.exe)."
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
