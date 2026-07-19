# Builds a classic Windows Setup.exe for Compressi (Inno Setup).
param(
    [ValidateSet('x64')]
    [string]$Platform = 'x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
$AppProject = Join-Path $Root 'Compressi.App\Compressi.App.csproj'
$IssFile = Join-Path $Root 'installer\Compressi.iss'
$OutputDir = Join-Path $Root 'installer\output'
$PublishDir = Join-Path $Root "Compressi.App\bin\$Configuration\net8.0-windows10.0.26100.0\win-$Platform\setup-publish"
$FfmpegDir = Join-Path $Root 'Compressi.App\Assets\ffmpeg'

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

Write-Host "Publishing Compressi ($Configuration, $Platform, unpackaged)..."
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

dotnet publish $AppProject `
    -c $Configuration `
    -r "win-$Platform" `
    -p:Platform=$Platform `
    -p:PublishProfile=win-x64-setup `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:SelfContained=true `
    -p:PublishTrimmed=false `
    -p:GenerateAppxPackageOnBuild=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$appExe = Join-Path $PublishDir 'Compressi.App.exe'
if (-not (Test-Path $appExe)) {
    throw "Published app not found: $appExe"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$setupIcon = Join-Path $Root 'Compressi.App\Assets\AppIcon.ico'
$isccArgs = @(
    "/DPublishDir=$PublishDir"
    "/DOutputDir=$OutputDir"
)
if (Test-Path $setupIcon) {
    $isccArgs += "/DSetupIcon=$setupIcon"
}

Write-Host "Compiling installer with Inno Setup..."
& $iscc @isccArgs $IssFile

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$setupExe = Get-ChildItem $OutputDir -Filter 'Compressi-Setup-*-x64.exe' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $setupExe) {
    throw "Setup.exe was not produced in $OutputDir"
}

# Stable name for GitHub releases/latest/download (versioned name kept for archives).
$stableSetup = Join-Path $OutputDir 'Compressi-Setup-x64.exe'
Copy-Item -LiteralPath $setupExe.FullName -Destination $stableSetup -Force

Write-Host ""
Write-Host "Installer ready:"
Write-Host "  $($setupExe.FullName)"
Write-Host "  $stableSetup"
Write-Host "  Size: $([math]::Round($setupExe.Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "Double-click the Setup.exe to install Compressi."
