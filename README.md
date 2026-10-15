# FastDM

FastDM is a Windows download manager with a Chromium browser extension.  The
desktop app owns downloads; the extension only sends links to the local app
after the user approves a pairing request.

## Project layout

| Path | Purpose |
| --- | --- |
| `FastDM/` | WinForms desktop application (`net10.0-windows`) |
| `FastDM/Core/` | Download models, engine, state storage, scheduler, traffic control, rules, updates |
| `FastDM/Network/` | Proxy, speed limit, saved credentials, FTP/SFTP transfer |
| `FastDM/Media/` | HLS/DASH detection and download, playlist (.m3u8) builder |
| `FastDM/YtDlp/` | yt-dlp process, cookies, site list and output parsing |
| `FastDM/UI/` | Main window in 8 partial files: `MainForm.cs` (fields, constructor, state) + `Layout`, `List`, `Downloads`, `Add`, `Settings`, `Shell`, `Bridge`; plus dialogs, theme and shared controls |
| `FastDM/Bridge/` | Local extension bridge, pairing, and `fastdm://` protocol handling |
| `FastDM/Resources/` | Desktop app icon |
| `FastDM.Package/` | Optional MSIX/Windows package project and its image assets |
| `FastDM.Extension/` | Chrome/Edge Manifest V3 extension |
| `FastDM.Tests/` | Dependency-free unit, bridge, protocol, privacy and configuration regression runner |
| `FastDM.IntegrationTests/` | Windows loopback HTTP tests for range downloads, cookies, cancellation and resume |
| `.github/workflows/` | GitHub Actions: CI build and tests |
| `FastDM.Extension/src/` | Extension source code: background, popup, options, shared code |
| `FastDM.Extension/tests/` | Node automated tests |
| `FastDM.Extension/tools/` | Extension packaging script |
| `docs/` | Design notes, privacy policy, tool requirements and audit/testing documentation |
| `scripts/` | Repeatable local build/test commands |

## Code conventions

- One namespace (`FastDM`); the folder tells the area, the file name tells the main type inside it.
- A file is named after its main type (`CredentialStore.cs`, `RemoteLister.cs`). Small helper types
  stay in the file of the type that owns them.
- A big class is split with `partial` as `Type.Part.cs` (`MainForm.Add.cs`, `DownloadEngine.Remote.cs`).
  All fields and the constructor stay in the main file (`MainForm.cs`).
- The static class `Engine` is called `DownloadEngine`, like `StreamEngine` and `YtDlpEngine`.

## Tools that are not in Git

`yt-dlp.exe`, a JavaScript runtime, and `ffmpeg.exe` with its required DLLs go in
`FastDM/Tools/`. The build copies that folder next to the app. Without these
optional local tools the app still runs, but video-site and stream-merge features
are unavailable. FastDM does not search `PATH` for these executables or download
them automatically. See [`docs/TOOLS.md`](docs/TOOLS.md) for requirements and the
local tool provenance review.

The privacy policy is at [`docs/Privacy.md`](docs/Privacy.md).

Generated folders (`bin`, `obj`, `.vs`, and `FastDM.Extension/dist`) are not
source code and are ignored by Git.

## Quick verification

From the repository root in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1
```

To run the desktop app directly:

```powershell
dotnet run --project FastDM/FastDM.csproj -c Debug -p:Platform=x64
```

To build and run the current source in an isolated temporary workspace without
ignored local tools, build outputs, or caches:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1
```

For the full browser-extension and MSIX manual checks, follow
[`docs/TESTING.md`](docs/TESTING.md).

## MSIX requirement

The desktop app can be built with the .NET SDK alone. Building
`FastDM.Package` requires a Visual Studio installation with the **MSIX Packaging
Tools** component (the Desktop Bridge MSBuild targets). Build with that
installation's MSBuild when the .NET CLI cannot locate those targets.
