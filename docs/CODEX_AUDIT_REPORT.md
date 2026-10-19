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

## Current-state security and release re-audit - 2026-10-19

This dated addendum records the follow-up implementation and checks. At inspection start the branch was `master`, HEAD was `62f2c5a`, and the worktree was clean. Local ignored tools and existing build outputs were preserved. The changes below are uncommitted; no commit, push, signing, installation, or publication was performed.

### Findings and implemented changes

- **SFTP trust on first use:** the first host key now requires explicit user approval that presents its SHA-256 fingerprint. Declined keys, changed keys, corrupt/unreadable trust files, and failures to persist an approved key are refused. Trust-file updates use a temporary file and atomic move. Automated tests cover approval, decline, matching and changed keys, corrupt state, and save failure. No live SFTP server was available for interactive verification.
- **HTTP range resume integrity:** byte-range downloads require a strong ETag or Last-Modified validator. Each range request sends `If-Range`, and malformed ranges or a server response indicating a changed representation fail before a final file is created. The validator is stored on the download item for persisted resume state. A loopback regression test changes the ETag between probe and transfer and verifies rejection.
- **Untrusted media and child output bounds:** process output lines and retained diagnostics are bounded; HLS/DASH manifests, expansions, templates and media candidates have explicit limits; XML DTD/entity resolution is disabled; oversized chunked manifests are rejected; media segment range lengths are checked; clear HLS segments are streamed to disk and encrypted payloads are capped.
- **Secret handling:** surfaced error messages and updater/video-preview diagnostics pass through the existing redaction helper. Regression coverage exercises URLs, query values, cookies, authorization headers, proxy passwords and Unicode diagnostics.
- **Package identity:** manifest checks report app version `1.0.13` and package version `1.0.13.0`. The clean-source package build succeeded without ignored local tools.

### Verification run on 2026-10-19

| Command/check | Result |
| --- | --- |
| `dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64` | **PASS**; 0 warnings, 0 errors. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1` | **PASS**; Debug desktop build (0 warnings/errors), package/version/tool-copy checks, 345 .NET tests, 30 loopback/process integration checks (0 failures), and 20 extension tests. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1` | **PASS**; source-only Release build (0 warnings/errors), package checks, unsigned MSIX bundle, 345 .NET tests, 30 integration checks (0 failures), 20 extension tests, and extension ZIP. |
| Clean-workspace MSIX bundle | **PASS as package generation only**; `FastDM.Package_1.0.13.0_x64.msixbundle`, 73,391,291 bytes. It was unsigned, not installed, and not submitted. |
| Clean-workspace extension ZIP | **PASS**; `FastDM-Extension-0.1.0.zip`, 34,467 bytes. |
| `node --test tests/ui-state.test.mjs` in `FastDM.Extension` | **PASS**; 2 tests. |
| NuGet vulnerability audit for app, unit-test, and integration-test projects | **PASS**; all three `dotnet list ... package --vulnerable --include-transitive` checks reported no vulnerable packages from configured sources. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1 -ForRelease` | **BLOCKED as intended**; exit 1 for `ffmpeg.exe`, `ffprobe.exe`, `avcodec-62.dll`, `avdevice-62.dll`, `avfilter-11.dll`, `avformat-62.dll`, `avutil-60.dll`, `swresample-6.dll`, and `swscale-9.dll`. Hashes/versions match the local lock, but upstream origin and redistribution provenance remain unverified. yt-dlp and Deno passed their local hash/version checks; Deno's Authenticode signature is valid. |
| `git diff --check` | **PASS after source and documentation edits**; no whitespace errors. Git emitted line-ending conversion notices only. |

### Release and manual checks still open

The current strict tool-provenance failure remains a release gate. FFmpeg's official legal guidance says optional GPL components can change the license and describes source, build configuration, dynamic-linking, and notice steps for its LGPL checklist; the current local FFmpeg bundle's origin, exact source/build, complete notices and redistribution terms have not been verified. See [FFmpeg legal guidance](https://www.ffmpeg.org/legal.html). The generated MSIX verifies packaging only; code-signing, installation, Store certification and submission were not performed. Microsoft Store policy 10.2.4 requires disclosure of non-integrated software dependencies in product metadata; policy version 7.20 is listed as effective 2026-10-22, after this audit date. Recheck it at submission in [Microsoft Store policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies).

The manual matrix in [`TESTING.md`](TESTING.md) remains unrun for live browser rendering/pairing and cookie profiles, screen-reader/high-contrast/high-DPI interaction, first-use SFTP prompt on a real server, FTP/SFTP authentication/navigation/cancellation, live YouTube extraction, reviewed FFmpeg merge scenarios, and signed Store install/upgrade/uninstall. The screenshots were not reproduced against live browser profiles. Automated tests and package creation do not establish those outcomes.

## UI/UX remediation follow-up — 2026-10-18

The audit above records the earlier full engineering and security review. This follow-up completes the separate five-phase UI/UX plan and keeps its stated boundary: dialogs, controls, theme, accessibility and user-visible presentation. It does not claim new changes to the download engine, network stack, browser bridge or extension.

At the start of this follow-up, the branch was `master` at `56577a2` after Phases 1–4. The worktree also contained a pre-existing edit to `FastDM.Package/FastDM.Package.wapproj` that enables package signing, disables revision auto-increment and sets a certificate thumbprint. That user-owned edit was not staged or changed. The completed UI phase commits are:

- Phase 1: `ca11bf2` — progress, preview and folder-selection fixes.
- Phase 2: `91e8d65` — DPI behavior and responsive dialogs.
- Phase 3: `53d7e49` — themed controls and dialogs.
- Phase 4: `56577a2` — download-list navigation and presentation.
- Phase 5: this handoff commit — URL validation, video-preview loading/retry, folder navigation and accessibility polish.

Phase 5 adds live link counting and inline URL/folder validation to Add URL; a themed indeterminate progress bar, thumbnail skeleton, disabled format inputs and retry action to the video preview; shell icons, navigable breadcrumbs and selected-size feedback to the remote-folder picker; and shared accessible names, descriptions and tab ordering for dialogs. Regression tests cover allowed link schemes, unsafe/malformed links, blank lines, original line numbers and empty input. The theme pass preserves validation colors and the borderless inputs inside their validation frames.

### Follow-up verification

| Command/check | Result |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1` | **PASS**; 320 .NET regression checks, 22 local integration checks and 20 extension tests passed; package/version checks passed. The Debug app build completed with 0 errors and 5 apphost-copy retry warnings because the running `FastDM` process (PID 9032) held `FastDM.exe`; the process was left running. The tool-provenance check retained the warnings documented above for the local FFmpeg bundle. |
| `dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64` | **PASS** after the final UI code change; 0 warnings, 0 errors. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1` | **PASS**; clean-copy Release app build had 0 warnings/errors; manifest/tool checks passed without ignored local tools; Visual Studio MSBuild 18.10.1 built `FastDM.Package_1.0.13.0_x64.msixbundle` (73,353,768 bytes); all 320 regression, 22 integration and 20 extension tests passed, and the extension ZIP was generated. The unique temporary copy was removed and its absence confirmed. This verifies package generation only; signing, installation and Store certification were not verified. |
| `dotnet build FastDM.Package/FastDM.Package.wapproj -c Release -p:Platform=x64` | **BLOCKED for the `dotnet` MSBuild invocation** because `C:\Program Files\dotnet\sdk\10.0.401\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props` is missing. The isolated package build passed through Visual Studio MSBuild, so the blocker is specific to the `dotnet` MSBuild target discovery path. |
| `git diff --check` | **PASS**; no whitespace errors. Git emitted line-ending conversion notices for LF-edited source/docs. |

The interactive Windows checks in [`TESTING.md`](TESTING.md) for validation colors, dialog tab order, screen-reader announcements, preview retry/loading states, breadcrumbs and size feedback were not run. The build and automated suites do not establish visual correctness or assistive-technology behavior. The manual FTP/SFTP, live browser-cookie and site-extraction limitations in the earlier audit also remain.

The original package-signing worktree edit remained unstaged after the Phase 5 commit. The FFmpeg/ffprobe provenance release gate remains a separate release blocker as described above.

## Second-pass UI/UX review — 2026-10-18

This pass began on branch `master` at `348aa6e` with a clean worktree. It followed the six UI areas in the attached objective and kept implementation changes to desktop/extension presentation and regression coverage. Download, network, scheduler, bridge, authentication, cookie, persistence and settings behavior were not changed. The extension `package.json` test command was left unchanged.

| Area | Finding and remediation | Status |
| --- | --- | --- |
| Main download list | Fixed column widths overflowed at the normal window/sidebar layout and did not adapt when resized. Added deterministic width allocation that favors Name/Progress, hides lower-priority columns only when required, restores all columns when space returns, and exposes hidden values in row tooltips and Details. | **Implemented; layout allocation regression-tested.** |
| Preferences | Fixed 640 px content rows and the 670 px traffic table could exceed the viewport. Content, path/argument rows, hints, checkboxes, and the traffic table now track available width; long labels/descriptions wrap and vertical scrolling remains available. | **Implemented; runtime visual check deferred.** |
| Batch Import | Video-quality and Save to labels did not follow bottom-anchored controls; fixed list columns could overflow. Labels now share the controls' anchors and the Link/Note columns adapt to list width. | **Implemented; runtime resize check deferred.** |
| Details Panel | Scrolling was difficult to discover, wheel movement was not clamped to content, and painted actions had no keyboard/accessibility children. Added a visible, clickable/draggable scrollbar; bounded wheel/Up/Down scrolling; focus cues; keyboard action selection/activation; and named accessible Save to/Link actions. Full path, URL and error values remain available through descriptions/tooltips and right-click copy. | **Implemented; screen-reader and runtime checks deferred.** |
| Extension popup/options | The disconnected popup could show two Open FastDM buttons; loading/task failures were unclear and options lacked a centered content width. Popup actions are now mutually exclusive by connection state, loading/recent-task feedback is explicit, options controls reflect loading/connected/unpaired/disconnected/error states, and the options page uses shared width/spacing tokens. English and Bangla keys remain in parity. | **Implemented; state logic and locale checks passed; browser rendering deferred.** |
| Branding | The desktop main-window title used “Fast DM” while the product name is “FastDM”. | **Fixed.** |

### Second-pass verification

| Command/check | Result |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1` | **PASS**; Debug build 0 warnings/errors; manifest and optional-tool integrity checks passed with the existing FFmpeg provenance warnings; 333 .NET regression checks, 22 loopback integration checks and 20 extension tests passed. |
| `dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64` | **PASS**; 0 warnings, 0 errors. |
| `node --test FastDM.Extension/tests/ui-state.test.mjs` | **PASS**; 2 UI-state regression tests. This file is run separately; `package.json` was not changed. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1` | **PASS**; source-only Release build and package metadata checks passed; Visual Studio MSBuild created an unsigned `FastDM.Package_1.0.13.0_x64.msixbundle` (73,373,807 bytes); 333 .NET checks, 22 integration checks and 22 extension tests passed, including the UI-state tests; extension ZIP created (34,467 bytes). The temporary workspace was removed by the script. This verifies package generation only, not signing or installation. |
| `git diff --check` | **PASS**; no whitespace errors. Git reported only LF-to-CRLF conversion notices for the rewritten extension scripts. |

No interactive WinForms window, Chrome/Edge popup, screen reader, high-DPI monitor, or accessibility inspection tool was available to this run. Therefore the six areas' runtime appearance, 520/700/900 px resize behavior, Preferences/Batch minimum sizes, popup/options rendering, actual pairing states, and high-DPI behavior remain manual checks listed in [`TESTING.md`](TESTING.md). Automated layout logic/build results do not establish those visual outcomes.
