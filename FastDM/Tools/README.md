# Optional local tools

Place independently reviewed Windows x64 tools in this folder for media features.

- yt-dlp.exe — video-site metadata and downloads
- deno.exe or qjs.exe — JavaScript runtime used by supported yt-dlp extractors
- ffmpeg.exe and its required DLLs — stream joining and media conversion
- ffprobe.exe — optional media inspection

These binaries are intentionally excluded from this source snapshot. Standard HTTP, FTP, and SFTP download functionality should not require them; video extraction and stream merging require the relevant tools. Do not place browser cookie databases, credentials, or private configuration files here.

Review docs/TOOLS.md and docs/tool-bundle.lock.json before staging any binary. Verify the upstream source, architecture, hashes or signatures, redistribution rights, dependent DLLs, license notices, and applicable source-offer requirements. Run the local integrity check from the repository root:

    powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1

Before release, also run:

    powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1 -ForRelease

The release check is intentionally strict and is expected to fail until release-tool provenance and licensing gaps documented in docs/TOOLS.md have been resolved.
