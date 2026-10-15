# Local download tools

FastDM does not download executable tools automatically. `FastDM/Tools/` is ignored by Git. A developer or release operator must stage reviewed tools there; the desktop build copies those files beside the application. FastDM looks in that app-local folder and its application directory. It does not search `PATH` for yt-dlp or FFmpeg, and its yt-dlp child process gets a restricted `PATH` containing only app-local and Windows system directories.

| Tool | Used for | Required for app startup? |
| --- | --- | --- |
| `yt-dlp.exe` | Video-site metadata and downloads | No |
| `deno.exe` or `qjs.exe` | JavaScript runtime for supported yt-dlp extractors | No |
| `ffmpeg.exe` and its required DLLs | Joining separate video/audio streams and media conversion | No |
| `ffprobe.exe` | Media inspection where available | No |

Without a tool, FastDM reports the missing capability and ordinary HTTP, FTP, and SFTP downloads remain available. A clean Git checkout does not include `FastDM/Tools/`; this directory is ignored by Git. MSIX packaging includes any tools found there, so run the release provenance gate before making a Store artifact. Do not commit private cookie files or credentials.

## Reproducible local verification

The audit snapshot is recorded in [`tool-bundle.lock.json`](tool-bundle.lock.json). To check any local files against those captured hashes and versions:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1
```

This check establishes only that local bytes match the recorded snapshot and that the recorded executable versions/signature match. It does not make an unknown file trustworthy. Before a release, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1 -ForRelease
```

The release mode requires every locked release tool and rejects entries whose upstream provenance is not verified. The current ignored FFmpeg/ffprobe build and its DLLs intentionally fail that gate. The repository does not yet have a deterministic, license-complete FFmpeg acquisition/staging source; do not create a Store package with that local bundle until the gate passes. No runtime or build step downloads and executes a tool.

For a clean checkout, leave `FastDM/Tools/` absent and use `scripts/Test-CleanWorkspace.ps1`. The desktop build, ordinary download engines and automated tests should still work; video extraction and stream merging are unavailable until an operator supplies reviewed tools.

## License and Store release notes

- The locked yt-dlp executable is from official release `2026.08.19`; its hash matches the release hash. yt-dlp is [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE), but its PyInstaller binary includes third-party components; distribute the corresponding [official third-party license notices](https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt) with it.
- The local Deno `2.9.7` Authenticode signature is valid and signed by Deno Land. Deno is [MIT licensed](https://github.com/denoland/deno/blob/main/LICENSE.md); preserve its notice.
- The local FFmpeg runtime reports LGPL version 3 or later. FFmpeg's [official legal guidance](https://ffmpeg.org/legal.html) requires verifying the exact build configuration and linked libraries; this build enables `version3`, uses shared DLLs, and does not show `--enable-gpl` or `--enable-nonfree`, but its exact source/build origin and licenses of every linked library are unknown, and the matching source bundle/notices are absent. A runtime banner is not sufficient redistribution evidence.
- The local FFmpeg/ffprobe executables are unsigned. Their audit hashes identify only this ignored workspace snapshot; they do not establish the publisher or source.
- [Microsoft Store policy 10.2.4](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies) requires a non-integrated software dependency to be disclosed at the beginning of the Store listing description. Recheck the in-force policy at submission; the [policy change history](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies-change-history) says policy 7.20 is scheduled to take effect on October 22, 2026. The Partner Center listing and certification notes have not been inspected.

The current worktree's tool versions and byte hashes are for audit repeatability only. If the files change, update the lock after independently verifying upstream source, signatures/checksums, architecture, required DLLs, license notices and source offer; do not simply bless a new local hash.

## Workspace artifact review (October 15, 2026)

The local ignored tool directory was present during the audit. Its `yt-dlp.exe` reported version `2026.08.19` and SHA-256 `66674953FE251B89F4D08C5F0E35E0728679BD67AB3D7D05C0562AF101DD3E7A`, matching the asset hash published for the corresponding [official yt-dlp release](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19). The executable's `--verbose` output showed bundled `yt_dlp_ejs 0.8.0` and Deno `2.9.7`; this was a live network smoke attempt, and YouTube returned `This video is unavailable` for the public test ID, so no extraction success is claimed. The local Deno binary reported version `2.9.7`, had a valid Authenticode signature signed by Deno Land, and matched its locked local hash.

The local `ffmpeg.exe` and `ffprobe.exe` are unsigned and have no useful Windows file-version metadata. Their runtime reports build `n8.1.3-14-g330caae0c1-20261003`; `-L` reports LGPL v3 or later, and `-buildconf` shows a shared build with `--enable-version3` and no `--enable-gpl`/`--enable-nonfree` flags. Their source/build origin and transitive notices remain **unverified**. The lock captures SHA-256 values for both executables and all seven accompanying DLLs, but these are local identity checks, not upstream provenance. These observations describe ignored files only; they do not establish what a future user or package contains. The CI clean checkout intentionally builds without these local binaries.
