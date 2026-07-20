# Downloads bundled FFmpeg/ffprobe (shared GPL build) into Compressi.App/Assets/ffmpeg.
# Shared build is used so ffmpeg.exe and ffprobe.exe share one set of DLLs (~100MB smaller
# than two static binaries). ffplay is intentionally omitted.
param(
    [string]$Url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'
)

$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
$DestDir = Join-Path $Root 'Compressi.App\Assets\ffmpeg'
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("compressi-ffmpeg-" + [Guid]::NewGuid().ToString('N'))
$ZipPath = Join-Path $TempRoot 'ffmpeg.zip'

New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null
New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

try {
    Write-Host "Downloading FFmpeg from $Url ..."
    Invoke-WebRequest -Uri $Url -OutFile $ZipPath -UseBasicParsing

    Write-Host "Extracting..."
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $TempRoot -Force

    $binDir = Get-ChildItem -Path $TempRoot -Recurse -Directory |
        Where-Object { $_.Name -eq 'bin' } |
        Select-Object -First 1

    if (-not $binDir) {
        throw "FFmpeg bin directory not found inside downloaded archive."
    }

    $ffmpeg = Join-Path $binDir.FullName 'ffmpeg.exe'
    $ffprobe = Join-Path $binDir.FullName 'ffprobe.exe'
    if (-not (Test-Path $ffmpeg) -or -not (Test-Path $ffprobe)) {
        throw "ffmpeg.exe / ffprobe.exe not found inside downloaded archive."
    }

    Get-ChildItem -LiteralPath $DestDir -Force |
        Where-Object { $_.Name -ne '.gitkeep' } |
        Remove-Item -Recurse -Force

    Copy-Item -LiteralPath $ffmpeg -Destination (Join-Path $DestDir 'ffmpeg.exe') -Force
    Copy-Item -LiteralPath $ffprobe -Destination (Join-Path $DestDir 'ffprobe.exe') -Force

    Get-ChildItem -LiteralPath $binDir.FullName -Filter '*.dll' -File |
        Copy-Item -Destination $DestDir -Force

    $copied = Get-ChildItem -LiteralPath $DestDir -File | Where-Object { $_.Name -ne '.gitkeep' }
    $totalMb = [math]::Round((($copied | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Write-Host "FFmpeg ready in $DestDir ($($copied.Count) files, ${totalMb} MB)"
}
finally {
    Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
