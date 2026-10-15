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
| `FastDM.Tests/` | Dependency-free test runner for the bridge (links the real source files) |
| `.github/workflows/` | GitHub Actions: CI build and tests |
| `FastDM.Extension/src/` | Extension source code: background, popup, options, shared code |
| `FastDM.Extension/tests/` | Node automated tests |
| `FastDM.Extension/tools/` | Extension packaging script |
| `docs/` | Design and architecture notes |
| `scripts/` | Repeatable local build/test commands |

## Code conventions

- One namespace (`FastDM`); the folder tells the area, the file name tells the main type inside it.
- A file is named after its main type (`CredentialStore.cs`, `RemoteLister.cs`). Small helper types
  stay in the file of the type that owns them.
- A big class is split with `partial` as `Type.Part.cs` (`MainForm.Add.cs`, `DownloadEngine.Remote.cs`).
  All fields and the constructor stay in the main file (`MainForm.cs`).
- The static class `Engine` is called `DownloadEngine`, like `StreamEngine` and `YtDlpEngine`.

## Tools that are not in Git

`yt-dlp.exe` (and `ffmpeg.exe` with its DLLs, for joining video and audio) go into
`FastDM/Tools/`. The build copies that folder next to the app. Without it the app
still runs, but yt-dlp and stream-merge downloads are unavailable.

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

For the full browser-extension and MSIX manual checks, follow
[`docs/TESTING.md`](docs/TESTING.md).

## MSIX requirement

The desktop app can be built with the .NET SDK alone. Building
`FastDM.Package` requires Visual Studio 2022 with the **MSIX Packaging Tools**
component (the Desktop Bridge MSBuild targets). Open `FastDM.slnx` in that
Visual Studio installation to build or deploy the package.
