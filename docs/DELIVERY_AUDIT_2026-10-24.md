# FastDM delivery audit — 2026-10-24

## Snapshot

- Source branch: master
- Source commit: 9cf47556cbcb64417d3fd73557015ceea9f7c36f
- This branch preserves the application source, existing features, tests, and assets; it adds a tool-staging note and this release-readiness report.
- Local executable tools and private/user-specific files are not included. See FastDM/Tools/README.md.

## Verified evidence

GitHub Actions CI run [39224026512](https://github.com/GOUROB2319/FastDM/actions/runs/39224026512) for the source commit completed successfully on 2026-10-19. The recorded completed steps include:

- Release build of FastDM/FastDM.csproj
- MSIX manifest, version, and asset validation
- Desktop regression, bridge, and protocol tests
- Download-engine integration tests
- Browser-extension tests

This is evidence from remote CI. No local Visual Studio or Windows package build is claimed in this report.

## Local setup

1. Extract the ZIP and open FastDM.slnx in Visual Studio 2026 with the .NET 10 SDK and Windows desktop development components installed.
2. Select FastDM as the startup project, use Debug | x64, and build/run.
3. For standard HTTP, FTP, and SFTP tests, optional media tools can remain absent. For video extraction and stream merging, stage independently reviewed tools in FastDM/Tools.
4. Run from the repository root:
   powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1
5. For an isolated clean source check, run:
   powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1
6. To load the browser extension manually, open edge://extensions or chrome://extensions, enable Developer mode, and load the FastDM.Extension folder.

## Known release gates — not marked as resolved

- docs/TOOLS.md and docs/STORE_RELEASE_CHECKLIST.md record that the previous local FFmpeg/ffprobe binaries and seven associated DLLs did not have independently verified upstream source and complete redistribution evidence. Do not package them until source provenance and licensing have been resolved.
- After staging candidate tools, run scripts/Test-ToolProvenance.ps1 -ForRelease and require it to pass. A matching hash alone does not establish trust or redistribution rights.
- Build and install the actual MSIX using Visual Studio's MSIX Packaging Tools/Desktop Bridge targets. Test upgrade, uninstall, fastdm:// protocol registration, startup opt-in/opt-out, and a clean Windows profile.
- Confirm package identity and publisher against Partner Center. Review current Microsoft Store policies, non-integrated dependency disclosures, privacy listing, age rating, screenshots, and certification notes before submission.
- CI does not guarantee that every video on every website remains downloadable. YouTube/TikTok behavior can change with site-side checks, login requirements, extractor updates, or media availability. Run a current, user-authorized smoke test; no universal download guarantee is made.
- Real browser profiles, interactive accessibility, real FTP/SFTP servers, Store installation, and Store certification are not covered by the cited CI run.

## Scope

This is a source snapshot and a release-readiness handoff, not a claim that every possible defect is fixed or that the package is approved for Microsoft Store publication. Existing code and regression tests were retained rather than deleting code without evidence that it is unused.
