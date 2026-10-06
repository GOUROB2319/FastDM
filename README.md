# FastDM

FastDM is a Windows download manager with a Chromium browser extension.  The
desktop app owns downloads; the extension only sends links to the local app
after the user approves a pairing request.

## Project layout

| Path | Purpose |
| --- | --- |
| `FastDM/` | WinForms desktop application (`net10.0-windows`) |
| `FastDM/Bridge/` | Local extension bridge, pairing, and `fastdm://` protocol handling |
| `FastDM/assets/` | Desktop app icon assets |
| `FastDM.Package/` | Optional MSIX/Windows package project and its image assets |
| `FastDM.Extension/` | Chrome/Edge Manifest V3 extension |
| `FastDM.Extension/src/` | Extension source code: background, popup, options, shared code |
| `FastDM.Extension/tests/` | Node automated tests |
| `FastDM.Extension/tools/` | Extension packaging script |
| `docs/` | Design and architecture notes |
| `scripts/` | Repeatable local build/test commands |

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
