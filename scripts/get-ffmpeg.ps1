# Downloads bundled FFmpeg/ffprobe into Compressi.App/Assets/ffmpeg.
param(
    [string]$Url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'
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

    $ffmpeg = Get-ChildItem -Path $TempRoot -Recurse -Filter 'ffmpeg.exe' |
        Where-Object { $_.DirectoryName -match '[\\/]bin$' } |
        Select-Object -First 1
    $ffprobe = Get-ChildItem -Path $TempRoot -Recurse -Filter 'ffprobe.exe' |
        Where-Object { $_.DirectoryName -match '[\\/]bin$' } |
        Select-Object -First 1

    if (-not $ffmpeg -or -not $ffprobe) {
        throw "ffmpeg.exe / ffprobe.exe not found inside downloaded archive."
    }

    Copy-Item -LiteralPath $ffmpeg.FullName -Destination (Join-Path $DestDir 'ffmpeg.exe') -Force
    Copy-Item -LiteralPath $ffprobe.FullName -Destination (Join-Path $DestDir 'ffprobe.exe') -Force

    Write-Host "FFmpeg ready in $DestDir"
}
finally {
    Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
