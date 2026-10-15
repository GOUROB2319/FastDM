# FastDM Full Engineering and Security Audit

**Audit date:** 2026-10-15
**Workspace:** `C:\dev\FastDM`
**Master plan:** [`CODEX_AUDIT_AND_REMEDIATION.md`](../CODEX_AUDIT_AND_REMEDIATION.md)

## Executive summary

The workspace was inspected before edits. It was on branch `master` at `f92c7d6568813b36073105b8221d3da13dfe822a` with a clean worktree. No pre-existing user changes were present. The ignored local `FastDM/Tools/` directory and its binaries were preserved. No reset, commit, publish, install, or release was performed. Build and package artifacts created for the clean-workspace check were confined to a unique temporary copy and removed by that script.

The verified changes address the supplied YouTube cookie errors, per-download cookie isolation, URL credential and proxy handling, bridge request bounds, range response integrity, yt-dlp process/update scheduling, merge-stage reporting, remote directory navigation, four-way download scheduling, credential-save truthfulness, package checks, and regression coverage. The two screenshots represent different failures: Image 1 is a browser cookie-database copy failure; Image 2 is a site sign-in/bot-verification challenge. They now have separate classifications and user guidance. A cookie-free retry is offered only as an explicit user action and does not modify the configured cookie source.

The source-only build, unsigned MSIX build, extension package, and automated suites passed. A release package is **not ready**: the release provenance gate correctly fails for the local FFmpeg/ffprobe executables and seven DLLs because their publisher/source and complete redistribution notices are unverified. Real FTP/SFTP servers, interactive UI accessibility, real browser profiles, signed Store installation, and successful live YouTube extraction were not verified.

## Baseline, scope, and safety

Reviewed the desktop app and forms, download and remote-transfer engines, yt-dlp preview/download/update path, browser extension and local bridge, credential/cookie/proxy handling, persistence/logging, scheduling, package metadata, build/test scripts, documentation, and ignored local tools. The original audit report was stale: it recorded a different baseline (`220a112`) and a prior `FastDM.slnx` user diff. The actual current baseline is the clean `f92c7d6` worktree above; those historical statements have been replaced here.

The current changes are limited to app source, regression tests, audit/release documentation, and verification scripts. No binaries, cookies, credentials, generated packages, or local `Tools/` contents were added to Git. Git emitted line-ending conversion notices for LF-edited files; `git diff --check` reported no whitespace errors.

## Findings and remediation

| Severity | Files | Finding and root cause | Remediation and test evidence | Status |
| --- | --- | --- | --- | --- |
| High | `Core/DownloadCookieScope.cs`, `Core/DownloadEngine.cs`, `Bridge/BridgeServer.cs`, cookie scope tests, integration tests | Browser cookies could be reused outside their intended request/site scope, including sibling hosts. | Filter by scheme/host/domain/path/secure/expiry and attach per download. Unit and loopback redirect/concurrency tests cover host-only and domain cookies. | **FIXED; regression-tested** |
| High | `Core/UrlCredential.cs`, `Core/StateStore.cs`, `Core/AppLog.cs`, `Bridge/ProtocolHandler.cs`, security tests | URL user-info could survive into persisted state, backups, display, logs, or protocol fallback. | Extract user-info into the credential store and remove it from stored/display URLs; migration and redaction preserve signed query strings. Unit and extension tests cover encoded credentials and fallback handling. | **FIXED; regression-tested** |
| High | `Bridge/BridgeServer.cs`, `Bridge/BridgeAuth.cs`, `Bridge/BridgeModels.cs`, bridge tests | The loopback extension bridge needed strict request bounds and complete responses under overload. | Enforce origin/pairing checks, URL allow-list, body/time/concurrency limits, cancellation, and explicit busy/rate-limit responses; local HTTP bridge tests cover these paths. | **FIXED; regression-tested** |
| High | `Core/DownloadEngine.cs`, `FastDM.IntegrationTests/Program.cs` | HTTP range responses with a wrong offset or byte count could be accepted; completion needed output validation. | Validate range status, `Content-Range`, and body length; loopback tests cover retry, cancel/resume, zero-byte and non-range responses. yt-dlp output is checked before completion. | **FIXED; regression-tested** |
| High | `YtDlp/YtDlp.cs`, `YtDlp/YtDlpDiagnostics.cs`, `YtDlp/YtDlpMetadata.cs`, `UI/YtPickerForm.cs`, `UI/YtDlpErrorForm.cs`, yt-dlp tests | Browser cookie DB errors, invalid cookie files, and bot verification were conflated; cache entries could cross a cookie-free retry. | Separate missing/locked/permission-denied DB, invalid/missing cookie-file, and bot/sign-in categories. Explicit retry omits cookie args and uses a separate cache key. Unit tests cover both screenshots, Brave DB-not-found, permission/lock cases, and args. | **FIXED; regression-tested** |
| High | `YtDlp/YtDlp.cs`, `YtDlp/YtDlpProxy.cs`, `YtDlp/YtDlpProcessEnvironment.cs`, `Core/AppLog.cs`, proxy/security tests | Proxy secrets or ambient executables could leak through child args/environment or be selected from `PATH`. | Pass authenticated proxy credentials in the child environment, URLs over redirected stdin, and use a restricted child `PATH`; redaction tests cover secrets. Higher-privilege process inspection can still see the environment. | **MITIGATED; regression-tested** |
| Medium | `Network/RemoteLister.cs`, `Core/DownloadEngine.Remote.cs`, `UI/FolderDownloadForm.cs`, `UI/MainForm.Add.cs`, integration tests | Folder selection recursively scanned immediately; ordinary HTML with directory-looking links could be mistaken for an index; listings lacked adequate bounds. | List immediate children and navigate; recurse only on explicit scan/download. Enforce HTTP index recognition, same-origin/path checks, page-size, entry, depth, and node limits. HTTP fixtures cover lazy navigation, traversal, unsupported pages, nested scan, entry caps, and a chunked oversized response. | **FIXED for HTTP; FTP/SFTP code-reviewed, real servers unverified** |
| Medium | `Core/DownloadEngine.Remote.cs`, `UI/MainForm.Add.cs`, `Network/RemoteLister.cs`, `UI/FolderDownloadForm.cs` | FTP/SFTP paths without a trailing slash were treated as files; playlist detection used an HTTP-only rule. | Probe FTP parent listings and SFTP attributes, including dotted directory names; prompt for credentials and validate folders across supported protocols. Build and project regressions pass; no live FTP/SFTP test server was available. | **IMPLEMENTED; FTP/SFTP manual verification deferred** |
| Medium | `Core/DownloadQueuePolicy.cs`, `Core/Models.cs`, `UI/MainForm.Downloads.cs`, `YtDlp/YtDlp.cs`, concurrency/integration tests | yt-dlp updates could overlap downloads; process and queue capacities were below the requested four-way default. | A four-slot process gate lets four children run while updates obtain exclusive capacity. Queue policy starts only available work; default/High cap is four. Tests exercise four HTTP transfers, cap-one serialization, four yt-dlp children, and update exclusion. | **FIXED; regression-tested** |
| Medium | `YtDlp/YtDlpMetadata.cs`, `YtDlp/YtDlpRetryPolicy.cs`, `UI/DetailsPanel.cs`, `UI/MainForm.List.cs`, `UI/MainForm.Downloads.cs`, stage tests | Late transfer progress could replace merge status, leaving misleading 100%, speed, ETA, or graph data. | Preserve merge/post-processing/output-verification stages across row, Details, and graph views; ignore late transfer callbacks and clear transfer metrics. Parser/stage tests pass. | **FIXED in code; visual UI check deferred** |
| Medium | `Network/CredentialStore.cs`, `UI/FolderDownloadForm.cs`, `UI/NetworkForm.cs`, credential test stubs | Credential writes could fail while the UI implied they were saved. | Return write/delete success; explain session-only storage after failure and do not apply Network changes when secure persistence fails. Build and project regression suite pass. | **FIXED; regression-tested** |
| Medium | `UI/MainForm.Layout.cs`, `UI/MainForm.cs`, `UI/PreferencesForm.cs` | Toolbar had a redundant standalone Pause control; narrow layouts could hide key actions early. | Remove standalone Pause while retaining Pause All and per-item pause; retain Open File/Open Folder longer in collapse order and lower minimum width. | **FIXED in code; interactive resize/accessibility checks deferred** |
| Medium | `scripts/Test-ToolProvenance.ps1`, `docs/tool-bundle.lock.json`, `scripts/Test-PackageManifest.ps1`, `docs/TOOLS.md`, `docs/STORE_RELEASE_CHECKLIST.md` | Packaging could silently include ignored local tools with unclear origin and incomplete notices. | Add a hash/version/signature lock, local integrity check, strict release gate, Store checklist, and clean-source package check. The strict gate identifies the exact unverified FFmpeg bundle. | **MITIGATED; release blocked on provenance/notices** |
| Medium | `Network/RemoteTransfer.cs`, `Network/NetworkConfig.cs` | SFTP host keys are trusted on first use without explicit user confirmation. | Changed keys are refused, but initial trust remains trust-on-first-use. An explicit confirmation needs a product decision and host-key UX. | **DEFERRED; see remaining risks** |

## Screenshot diagnosis

- **Image 1:** yt-dlp reports it could not copy Chrome's cookie database. This is classified as a browser database copy/lock failure, with advice to close browser/background processes or select another supported source. FastDM does not kill the browser or weaken cookie protection.
- **Image 2:** yt-dlp reports “Sign in to confirm you're not a bot.” This is classified as bot/sign-in verification, separate from the database error. FastDM permits a user-selected supported cookie source but does not automate CAPTCHA or bypass access controls.
- Automated classification and rendering logic were tested. The exact screenshots were not reproduced against a real browser profile. A live public-video smoke attempt returned upstream `This video is unavailable` (exit code 1); no successful extraction is claimed.

## Tool provenance and release gate

The ignored local tool snapshot is identified in [`tool-bundle.lock.json`](tool-bundle.lock.json). Default verification passed: yt-dlp `2026.08.19` matched the official release hash; Deno `2.9.7` matched its hash and had a valid Deno Land Authenticode signature. `qjs.exe` was absent. FFmpeg and ffprobe report `n8.1.3-14-g330caae0c1-20261003`; their hashes and seven DLL hashes match the local lock, but the binaries are unsigned and upstream origin, linked-library notices, source offer, and full redistribution terms remain unverified. Their LGPL runtime banner/build flags alone do not establish redistribution compliance.

`Test-ToolProvenance.ps1 -ForRelease` exited 1, naming `ffmpeg.exe`, `ffprobe.exe`, `avcodec-62.dll`, `avdevice-62.dll`, `avfilter-11.dll`, `avformat-62.dll`, `avutil-60.dll`, `swresample-6.dll`, and `swscale-9.dll` as having unverified release provenance. Do not distribute the current local tool bundle until a reviewed source/build and complete notices are available.

## Verification results

Final verification was run after the last source change.

| Command/check | Result |
| --- | --- |
| `dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64` | **PASS**; 0 warnings, 0 errors. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1` | **PASS**; Debug build, manifest/version/assets/tool-copy check, 310 .NET regression tests, 22 loopback/process integration checks, and 20 extension tests. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1` | **PASS**; source-only Release build, metadata checks without ignored tools, unsigned MSIX bundle and extension ZIP in the temporary copy, plus the same .NET, integration, and extension suites. Temporary copy removed by the script. |
| Clean-workspace MSIX | **PASS as an unsigned build only**; `FastDM.Package_1.0.13.0_x64.msixbundle`, 73,269,442 bytes. Not signed, installed, certified, or submitted. |
| Clean-workspace extension pack | **PASS**; `FastDM-Extension-0.1.0.zip`, 33,273 bytes. |
| `node --test FastDM.Extension/tests/bridge.test.mjs FastDM.Extension/tests/locales.test.mjs` | **PASS**; 18 tests. `npm test` in the project and clean-workspace scripts also covered cookie tests, for 20/20. |
| `scripts/Test-ToolProvenance.ps1` | **PASS with warnings**; local hashes/versions checked; unverified FFmpeg provenance reported. |
| `scripts/Test-ToolProvenance.ps1 -ForRelease` | **BLOCKED as intended**; exit 1 for the nine unverified FFmpeg/ffprobe files listed above. |
| `dotnet list FastDM/FastDM.csproj package --vulnerable --include-transitive`; `dotnet list FastDM.Tests/FastDM.Tests.csproj package --vulnerable --include-transitive`; and `dotnet list FastDM.IntegrationTests/FastDM.IntegrationTests.csproj package --vulnerable --include-transitive` | **PASS**; NuGet reported no vulnerable packages for all three projects using the configured sources. The extension has no npm dependencies. |
| `git diff --check` | **PASS**; no whitespace errors. Git printed line-ending conversion notices only. |

The package manifest check reported desktop app `1.0.13` and MSIX `1.0.13.0`; the clean source checkout had no local video tools and still built. The unsigned MSIX build verifies packaging, not Store readiness or tool redistribution rights.

## Remaining risks and manual verification

- Obtain and independently verify FFmpeg/ffprobe source, publisher/build provenance, linked-library licenses, notices, and source offer before a release. Re-run the strict provenance gate against a reviewed bundle.
- Explicitly decide the SFTP first-use host-key UX. Changed fingerprints are rejected, but initial trust has no confirmation step.
- Run the manual matrix in [`TESTING.md`](TESTING.md): 520/700/900 px and maximized toolbar/table layout, DPI/text scaling, themes, keyboard/screen reader/high contrast, merge success/failure/cancel with a reviewed FFmpeg build, actual FTP/SFTP navigation/auth/symlink/cancellation, browser extension install/pairing, browser cookie profiles, and signed Store install/upgrade/uninstall.
- No real FTP or SFTP endpoint was available; automated remote directory tests use a loopback HTTP fixture. SFTP behavior was code-reviewed; first-key TOFU remains deferred.
- Live YouTube extraction, signed-in cookie use, and screenshot reproduction remain unverified. Site behavior, account requirements, network, and current yt-dlp support can change independently of these local tests.
- Store identity/listing and certification were not inspected in Partner Center. Microsoft's current [Store policy](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies) requires disclosure of non-integrated software dependencies; review it again at submission.
- Signed package installation and Store submission were not performed.

## Changed files by area

- **yt-dlp behavior and UI:** `FastDM/YtDlp/YtDlp.cs`, `YtDlpDiagnostics.cs`, `YtDlpMetadata.cs`, `YtDlpRetryPolicy.cs`; `FastDM/UI/YtPickerForm.cs`, `YtDlpErrorForm.cs`, `MainForm.Add.cs`, `MainForm.Downloads.cs`, `MainForm.List.cs`, `DetailsPanel.cs`.
- **Remote browsing, credentials, scheduling, and settings:** `FastDM/Core/DownloadEngine.Remote.cs`, `Models.cs`, new `DownloadQueuePolicy.cs`; `FastDM/Network/RemoteLister.cs`, `CredentialStore.cs`; `FastDM/UI/FolderDownloadForm.cs`, `NetworkForm.cs`, `PreferencesForm.cs`, `MainForm.Layout.cs`.
- **Bridge and regression coverage:** `FastDM/Bridge/BridgeServer.cs`; `FastDM.Tests/` and `FastDM.IntegrationTests/Program.cs`, including new `DownloadConcurrencyTests.cs`.
- **Packaging and audit documentation:** `scripts/Test-PackageManifest.ps1`, new `scripts/Test-ToolProvenance.ps1`, new `docs/tool-bundle.lock.json`, `docs/TOOLS.md`, `docs/TESTING.md`, new `docs/STORE_RELEASE_CHECKLIST.md`, and this report.

The master plan remains the source of task requirements; this report records the current implementation and verified outcomes.
