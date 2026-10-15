# Local download tools

FastDM does not download executable tools automatically. Place supported tools in `FastDM/Tools/`; the desktop build copies those files beside the application. FastDM looks in that app-local folder and its application directory. It does not search `PATH` for yt-dlp or FFmpeg, and its yt-dlp child process gets a restricted `PATH` containing only app-local and Windows system directories.

| Tool | Used for | Required for app startup? |
| --- | --- | --- |
| `yt-dlp.exe` | Video-site metadata and downloads | No |
| `deno.exe` or `qjs.exe` | JavaScript runtime for supported yt-dlp extractors | No |
| `ffmpeg.exe` and its required DLLs | Joining separate video/audio streams and media conversion | No |
| `ffprobe.exe` | Media inspection where available | No |

Without a tool, FastDM reports the missing capability and ordinary HTTP, FTP, and SFTP downloads remain available. A clean Git checkout does not include `FastDM/Tools/`; this directory is ignored by Git. MSIX packaging includes any tools found there, so verify their source and hashes before distributing a package. Add tool updates deliberately. Do not commit private cookie files or credentials.

## Workspace artifact review (October 15, 2026)

The local ignored tool directory was present during the audit. Its `yt-dlp.exe` reported version `2026.08.19` and SHA-256 `66674953FE251B89F4D08C5F0E35E0728679BD67AB3D7D05C0562AF101DD3E7A`, matching the asset hash published for the corresponding [official yt-dlp release](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19). The local Deno binary reported version `2.9.7` and had a valid Authenticode signature.

The local `ffmpeg.exe` and `ffprobe.exe` were unsigned and had no useful file-version metadata. Their build source could not be established from the workspace, so their provenance is **unverified**. These observations describe the local ignored files only; they do not establish what a future user or package contains. The CI clean checkout intentionally builds without these local binaries.
