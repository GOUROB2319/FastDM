# FastDM security and engineering audit

**Audit date:** 2026-10-15
**Workspace:** `C:\dev\FastDM`
**Plan:** [`CODEX_AUDIT_AND_REMEDIATION.md`](../CODEX_AUDIT_AND_REMEDIATION.md)

## Scope and workspace preservation

The audit covered the desktop application and download/media engines, URL and
credential handling, browser extension and local bridge, logging, user-facing
diagnostics, packaging/version metadata, CI, tests, and repository
documentation. The workspace inventory included tracked source, ignored build
outputs/caches, packaging outputs, and the ignored local `FastDM/Tools` bundle.

At the start, Git was on `master`, four commits ahead of `origin/master`, with
no modified tracked files. The user-provided plan was untracked. No reset or
cleanup was performed. At the end, the plan and an IDE `.vscode/settings.json`
were still untracked and left untouched; generated build/package outputs remain
local. The source edits and added tests below are the audit work.

## Findings and remediation

| Severity | Finding | Remediation and evidence | Status |
| --- | --- | --- | --- |
| High | A shared cookie container could carry one download's browser cookies into another request, including requests to a different host. Cookie expiry and host/path/secure scoping also needed enforcement. | Added per-download cookie scopes, expiry handling, cookie-free shared clients, and non-persisted cookie fields. Unit and loopback tests cover scope, expiry, and two simultaneous downloads to the same host with distinct cookies. | Fixed; regression-tested |
| High | URL user-info could persist in download state, backups, logs, command construction, or the `fastdm://` fallback. | Added URL credential extraction into the credential store, stripped user-info from stored/displayed/yt-dlp/protocol URLs, sanitized existing state and backups, and redacted credentials, tokens, query/fragment data, and local paths from logs. Tests cover encoded credentials, query preservation, state migration, protocol fallback, and redaction. | Fixed; regression-tested |
| High | The local bridge accepted work without firm body-size, request-time, and concurrency bounds; rate-limit responses could leave excess background work. | Added bounded request bodies and duration, strict UTF-8 parsing, a 32-request concurrency ceiling, explicit busy responses, cancellation, and bounded rate-limit behavior. Bridge regressions cover oversized and concurrent requests. | Fixed; regression-tested |
| High | Download segments trusted server range behavior too readily, risking corrupt output when a server returned a mismatched `206`/`Content-Range` or inconsistent bytes. | Added range and length validation before accepting segment data. Unit and loopback tests cover exact bytes, bad ranges, redirects, retries, server errors, empty content, cancellation, and resume. | Fixed; regression-tested |
| High | yt-dlp tool discovery through ambient `PATH` could run an unintended executable; URLs and proxy credentials were exposed in process arguments. | yt-dlp and FFmpeg resolution is app-local. The yt-dlp child gets a restricted `PATH`; URL input is sent over UTF-8 stdin, and authenticated proxy settings use child environment variables. Tests cover URL input, proxy construction, and restricted child `PATH`. | Mitigated; see residual risks |
| Medium | Cookie source selection and yt-dlp failures could be misleading, including silently falling through when a configured cookie file was invalid. | Added explicit cookie-source states and actionable failure classification; invalid configured files now produce their own error. Added a resizable error dialog with redacted technical details and next steps. Unit tests cover cookie-source and error classification. | Fixed; regression-tested |
| Medium | A child process could outlive cancellation or stop producing useful output for Unicode text. | Process cancellation now kills and waits for the process tree; output is drained and decoded as UTF-8. A real child-process integration check verifies Bengali/Unicode output and cancellation. | Fixed; integration-tested |
| Medium | Browser cookie expiration was not carried through the extension bridge, and expired cookies could be forwarded. | The extension now forwards expiration metadata and excludes expired cookies; bridge/model tests cover it. Cookie values remain in memory only and are not serialized with download state. | Fixed; regression-tested |
| Medium | Extension packaging could overwrite an existing ZIP, and CI did not explicitly validate package metadata or ignored-tool copying. | The packer stages to a unique temporary directory and selects an unused archive name. Added app/MSIX version and asset checks, optional-tool copy checks, and CI coverage. Packaging was run successfully; see results below. | Fixed; locally verified |
| Low | Nullable warnings and package/version drift could obscure regressions. | Resolved the observed nullable warnings and added version/assets validation. App builds completed with zero warnings; the manifest check passed. | Fixed; verified |
| Medium | SFTP first-use host-key handling trusts the first key automatically. | This pre-existing trust-on-first-use behavior remains; changed keys are rejected, but initial key confirmation is not yet explicit. | Deferred |

## User-provided screenshots

- **Image #1:** Chrome could not copy its cookie database. This is diagnosed as
  a browser-profile database access/lock problem, with export or another
  browser offered as the next step. The app does not attempt to bypass browser
  profile protections.
- **Image #2:** YouTube requested sign-in to confirm that the visitor is not a
  bot. This is reported as a site authentication/verification challenge, not a
  cookie-database copy failure. The app does not bypass the challenge; the user
  can use a permitted signed-in browser cookie source or download the content
  manually if the site requires it.

## Verification results

All results below are from this workspace on 2026-10-15.

| Command or artifact | Result |
| --- | --- |
| `dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64` | Passed; zero warnings and zero errors. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-PackageManifest.ps1` | Passed; app/package version is `1.0.13` / `1.0.13.0`, required assets exist, and locally available tools were copied to build output. |
| `dotnet run --project FastDM.Tests -c Release` | Passed; 255 checks, zero failures. |
| `dotnet run --project FastDM.IntegrationTests -c Release` | Passed; 11 loopback/process checks, zero failures. |
| `npm test` in `FastDM.Extension` | Passed; 20 tests, zero failures. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1` | Passed; Debug build (zero warnings), package check, 255 .NET checks, 11 integration checks, and 20 extension tests. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1` | Passed from a unique temporary copy without ignored tools/build outputs: clean Release build, package check, 255 .NET checks, 11 integration checks, and 20 extension tests. The script removed only its temporary copy. |
| VS 18 `MSBuild.exe FastDM.Package/FastDM.Package.wapproj /m /p:Configuration=Release /p:Platform=x64 /v:minimal` | Passed; generated the MSIX bundle and upload package. Three NuGet compatibility warnings were reported for `System.Formats.Asn1 10.0.10`, `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2`, and `Microsoft.Extensions.Logging.Abstractions 8.0.3` targeting the packaging project's `net451` restore. |
| VS 18 `MSBuild.exe FastDM.slnx /m /p:Configuration=Release /p:Platform=x64 /v:minimal` | Passed; built the desktop and packaging projects and created scale-specific MSIX packages plus the bundle/upload package. The same three packaging compatibility warnings were reported. |
| `dotnet build FastDM.Package/FastDM.Package.wapproj -c Release -p:Platform=x64` | Blocked: the installed .NET SDK could not find `Microsoft.DesktopBridge.props` (`MSB4019`). The Visual Studio MSBuild run above supplied those Desktop Bridge targets and completed. |
| `powershell -ExecutionPolicy Bypass -File tools/pack.ps1` from `FastDM.Extension` | Passed; created `FastDM.Extension/dist/FastDM-Extension-0.1.0-1.zip` (33,273 bytes, 22 entries), SHA-256 `81643D481DDB30FD15E1375021C9D5E7950D359C8E1F4C3CFA91C2CC5C18D980`. |
| `git diff --check` | Passed. Git printed line-ending conversion notices for three edited text files; no whitespace errors. |

The final MSIX bundle is `FastDM.Package_1.0.13.0_x64.msixbundle` (204,520,361
bytes) and was reported `NotSigned`. The successful packaging result verifies
generation only; installation, signing, Store submission, and deployment were
not tested. The local ignored tool bundle was present during packaging, so
those tools may be embedded in this local artifact.

The local `yt-dlp.exe` was version `2026.08.19` and its SHA-256 matched the
[official yt-dlp release asset](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19).
Local Deno `2.9.7` had a valid Authenticode signature. Local `ffmpeg.exe` and
`ffprobe.exe` were unsigned and had no useful version metadata; their source
could not be established. See [`TOOLS.md`](TOOLS.md). This does not establish
the provenance of tools in another checkout or release package.

## Remaining verification and risks

- The clean-workspace run confirms a source-only checkout builds and tests
  without optional local tools. It does not exercise yt-dlp, Deno, FFmpeg,
  ffprobe, or the actual merge/conversion path.
- No live YouTube/site, authenticated-proxy, browser-profile-lock, or
  production-network download was run. Loopback integration tests are not a
  substitute for those services or accounts.
- No interactive Windows UI pass was performed for 100/125/150/200% DPI,
  small screens, keyboard-only navigation, screen readers, translated strings,
  or light/dark themes. The new diagnostic dialog builds and its classification
  logic is tested, but its visual accessibility needs manual validation.
- Chrome/Edge extension installation and end-to-end pairing, actual browser
  cookie export, fastdm protocol activation, and SFTP/HLS/DASH behavior were
  not manually exercised in a browser or against a real server.
- Signed query values remain part of local download URLs because servers may
  require them for resume. User-info is removed and logs redact query strings,
  but local state-file access remains sensitive. Credential-store writes clear
  temporary buffers; authenticated proxy values exist in the child environment
  while yt-dlp runs and may be visible to sufficiently privileged process
  inspection tools.
- The MSIX bundle is unsigned, local FFmpeg provenance is unverified, and the
  SFTP first-use trust behavior described above is still deferred. Do not treat
  this workspace's package as a signed or distribution-ready release.

## Files changed

The implementation changes are in `FastDM/`, `FastDM.Extension/`,
`FastDM.Tests/`, new `FastDM.IntegrationTests/`, `.github/workflows/ci.yml`,
`scripts/`, `README.md`, and `docs/Privacy.md` / `docs/TESTING.md`. This report
records verification rather than replacing the master plan.
