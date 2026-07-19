<p align="center">
  <img src="Compressi.App/Assets/AppLogo.png" alt="Compressi" width="96" />
</p>

<h1 align="center">Compressi</h1>

<p align="center">
  Fast, local video compression for Windows — powered by FFmpeg.
</p>

<p align="center">
  <a href="https://github.com/thomasboyle/compressi/releases/latest/download/Compressi-Setup-x64.exe">
    <img src="https://img.shields.io/badge/download-latest-0A7A3E?style=for-the-badge&logo=windows&logoColor=white" alt="Download latest" />
  </a>
</p>

<p align="center">
  <a href="https://github.com/thomasboyle/compressi/releases/latest"><img src="https://img.shields.io/github/v/release/thomasboyle/compressi?style=flat-square&label=release&color=0A7A3E" alt="Latest release" /></a>
  <a href="https://github.com/thomasboyle/compressi/releases"><img src="https://img.shields.io/github/downloads/thomasboyle/compressi/total?style=flat-square&label=downloads&color=1F6FEB" alt="Downloads" /></a>
  <img src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows x64" />
  <img src="https://img.shields.io/badge/encoder-AV1%20%2B%20GPU-111827?style=flat-square" alt="AV1 + GPU" />
</p>

## Features

- **8 MB Target** — size videos for Discord and chat apps
- **Presets** — Ultra, 8 MB Target, and Balanced
- **Formats** — MP4, MKV, WebM
- **GPU encode** — NVENC / QSV / AMF when available, CPU fallback otherwise
- **History** — reopen past outputs from the app

## Install

1. Download the [latest installer](https://github.com/thomasboyle/compressi/releases/latest/download/Compressi-Setup-x64.exe)
2. Run `Compressi-Setup-x64.exe`
3. Drop a video on Compress and hit **Start Compression**

Requires Windows 10 (1809+) or Windows 11, x64.

## Build

```powershell
# App
dotnet build Compressi.slnx -c Release

# Installer (needs Inno Setup 6 + FFmpeg binaries in Compressi.App/Assets/ffmpeg)
.\scripts\build-installer.ps1
```

## License

App source: see repository. Bundled FFmpeg is LGPL/GPL depending on build — [ffmpeg.org](https://ffmpeg.org/).
