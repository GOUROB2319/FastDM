# FastDM UI/UX & Architecture Remediation Plan

**Audit snapshot:** public `master` at `9cf47556cbcb64417d3fd73557015ceea9f7c36f` (2026-10-19)  
**Plan updated:** 2026-10-24  
**Scope:** Windows WinForms UI, video preview/thumbnails, loading/error states, maintainability, verification and release readiness.

> This is a code-and-document review of the public repository. It is not a claim that the GUI was launched or every site was tested. The last audit report records successful automated suites but also says interactive Windows UI checks, live browser-cookie tests and successful live YouTube extraction were not verified. The latest master commit also needs a fresh build/test run.

## Executive summary

FastDM already has meaningful separation between `Core`, `Network`, `Media`, `YtDlp`, `Bridge`, `UI`, the Chromium extension and automated test projects. A rewrite from scratch is not the safest first move. The recommended route is incremental: fix reproducible user-facing failures, define the UI state and design system, then extract testable presentation/services from the large WinForms forms.

The first confirmed UI defect is the thumbnail path: `YtPickerForm.TryTrustedThumbnailUri` originally accepted only HTTPS URLs under `ytimg.com` and `youtube.com`, so valid TikTok thumbnails and many other extractor CDN URLs were rejected. The invalid-URL early return also bypassed the code that hides the loading skeleton, leaving a permanent-looking loading placeholder. Metadata parsing retained only one thumbnail URL, so it had no useful fallback when a CDN URL was stale or inaccessible.

A focused change is staged on branch **`fix/ui-thumbnail-fallback-audit`**: candidate lists are retained, the fetch path can try alternate URLs, the loading placeholder has an explicit “unavailable” state, and the fetch path rejects non-HTTPS/private destinations. Regression tests were added for TikTok CDN candidates and URL policy. **These changes have not yet been built or run in a Windows environment; treat them as pending validation until CI and manual tests pass.**

## Findings: evidence vs. pending diagnosis

### Confirmed from source

| Area | Finding | Impact |
| --- | --- | --- |
| Video thumbnails | The original URL host allow-list was YouTube-only. | TikTok and other site thumbnails were rejected before the image request. |
| Thumbnail loading state | An early return on a rejected URL could leave the skeleton visible. | The preview looks stuck even after metadata has loaded. |
| Thumbnail fallback | The parser exposed one URL instead of an ordered candidate collection. | A failed/stale CDN URL had no fallback. |
| Main download list | The owner-drawn list has name/status/progress data but no per-video thumbnail. | Thumbnail fixes in the video picker do not add thumbnails to the main list. This is a separate UX feature. |
| UI maintenance | `MainForm.Layout.cs`, `MainForm.Add.cs`, `PreferencesForm.cs`, `FolderDownloadForm.cs`, and `YtPickerForm.cs` are large files. `MainForm` is split into partial files, but partial files alone do not separate state or business logic. | Small visual changes can have broad side effects; logic is harder to test in isolation. |
| Custom rendering | The main list uses owner drawing and custom Win32 header handling. | Keep it only where needed; verify keyboard navigation, DPI, theme and redraw behavior rather than assuming native controls behave normally. |
| Verification gap | The previous report says interactive resize, actual high-DPI, screen-reader, browser popup rendering and live site checks were not performed. | Unit tests and successful compilation do not prove the UI looks correct at runtime. |
| Tool/release reproducibility | `docs/TOOLS.md` records yt-dlp `2026.08.19`; release-tool provenance also records an unverified local FFmpeg/ffprobe bundle. | Recheck current upstream versions, SHA/signature, redistribution licence notices and the release gate before packaging. |

### Not yet confirmed

The user reports that some screens get stuck and text/layout appear broken. I could not reproduce those symptoms from the public source without running the Windows application and seeing the affected screen. The exact trigger may be DPI/window size, a long metadata request, stale async completion, an extractor/network failure, or a layout edge case. Diagnose each against a reproducible test rather than applying broad visual changes and guessing.

## Target UI direction

### Main window

1. **Top app bar:** FastDM identity, one prominent **Add URL** action, paste-link affordance, batch import and a compact overflow for less frequent actions. Avoid duplicating the same action in multiple places.
2. **Left navigation:** All downloads, Active, Queued, Completed and Failed with live counts. Site/media filters belong below core state filters; avoid a long list of equally weighted controls.
3. **Main content:** a clear section title, search, sort and view controls, then a responsive list. For video entries, reserve a consistent 40×40 thumbnail; ordinary files use a file-type icon. Display filename and source host on separate lines, a readable progress bar, state, speed and ETA. Secondary columns may collapse at narrow widths but information must remain available in Details/tooltips.
4. **Selection/details:** show a coherent details panel for the selected item: title/source, progress or processing stage, destination, safe error summary, and the actions valid for that state. Pause/resume/retry/cancel must never look enabled when that transition is invalid.
5. **Empty/loading/error states:** each state has a short explanation and an action. A loading indicator must end in success, retryable failure, timeout or cancellation; do not leave a spinner/skeleton without a next step.
6. **Responsive behavior:** preserve the primary add action and key download controls at 520, 700 and 900 logical-pixel window widths, maximized and during resize. Prefer wrapping/scrolling over clipped labels. Test long English text, Unicode titles and Bangla text where the app displays localized or user-supplied text.

### Video preview and Add URL flows

- Use explicit states: **idle → loading metadata → preview ready / metadata error**, and independently **loading thumbnail → image ready / fallback**. Thumbnail failure must not disable a valid download choice.
- Keep a candidate list, try a bounded number of URLs in order, cap response bytes and dimensions, validate redirects, and cancel stale requests. Do not let a previous preview write an image into a newly selected video.
- If the thumbnail fails, show a neutral icon and “Thumbnail unavailable”; keep title, source, format, destination and download actions usable.
- Keep technical diagnostics secondary/collapsible and redacted; make the primary error message understandable. Add actionable Retry / Open settings options only when those actions really work.
- Distinguish **metadata loading**, **thumbnail loading**, **video/audio transfer**, **merge/post-processing**, and **output verification**. A 100% transfer bar must not imply the whole operation is done if merging is still running.

### Visual system

- Continue using the existing central `Theme` rather than adding unrelated one-off colors to each form.
- Define/normalize semantic tokens for window, surface, raised surface, border, primary text, secondary text, accent, success, warning and error. Keep light/dark/system themes equivalent.
- Use a consistent 8-point spacing rhythm, title/body/secondary text hierarchy, button sizes, field heights and focus styles. Respect the app's DPI scaling policy and avoid double-scaling.
- Use one shared set of controls/patterns for loading, inline validation, empty states, errors, tooltips and confirmation dialogs.
- Custom drawing should remain deliberate and covered by accessibility labels, keyboard navigation and DPI tests.

## Prioritized implementation phases

### P0 — Establish a trustworthy baseline

- Preserve the existing branch/worktree; work on a feature branch and review a PR before merging to protected `master`.
- Run `scripts/Test-Project.ps1`, the clean-workspace script, Release build and `git diff --check` at the exact commit being reviewed.
- Record exact build output and distinguish old failures from new ones.
- Recheck the bundled yt-dlp against the current official release and upstream security advisories. Verify official hashes/signatures, version, third-party licence notices, runtime requirements and package provenance before updating the lock.
- Keep the FFmpeg/ffprobe provenance gate blocking release until the exact build source, licences and required notices are verifiable.

### P1 — Repair thumbnail reliability (change staged; validation pending)

- Parse all usable thumbnail candidates from `thumbnail` and `thumbnails[]`, keep a bounded ordered list, and try alternatives on network/format failure.
- Allow legitimate HTTPS CDN hosts without a YouTube-only hard-coded allow-list, while preventing non-HTTPS, credential-bearing URLs and private/loopback/link-local destinations. Resolve and connect only to public IP addresses for thumbnail requests; validate redirects too.
- Ensure every code path leaves the loading state. Add request generation/cancellation checks to avoid stale async results.
- Add regression tests for TikTok-style thumbnail metadata, alternate URLs, bad schemes, local/private addresses, duplicate candidates and no-thumbnail fallback.
- Manually verify YouTube, TikTok, another supported extractor, invalid link, unreachable thumbnail, timeout, redirect and no-internet scenarios. A thumbnail failure must not prevent a valid download.

### P2 — Redesign the main download experience

- Make a reusable row/view model that separates text/status computation from owner-drawn painting.
- Add a thumbnail/icon slot to rows without making network calls from the paint loop. Fetch thumbnails off the UI thread via a bounded cache/service; dispose replaced bitmaps.
- Make list columns responsive, maintain stable selection while rows refresh, avoid unnecessary full list rebuilds, and ensure progress repaint does not flicker.
- Keep the same state/action mapping for list rows, details, context menus, and tray actions.
- Build an agreed visual reference for 520/700/900 widths, 100/125/150/200% DPI, light/dark theme and maximized state before treating the redesign as complete.

### P3 — Fix forms and workflows consistently

Audit these forms individually: `AddUrlForm`, `BatchImportForm`, `FolderDownloadForm`, `YtPickerForm`, `MediaPickerForms`, `NetworkForm`, `PreferencesForm`, `ScheduleForms`, and `YtDlpErrorForm`.

For each form, check:
- clear title and primary/secondary action hierarchy;
- long labels/paths/titles and resize/scroll behavior;
- loading, empty, validation, error, timeout and retry states;
- keyboard tab order, Enter/Escape, focus return and screen-reader names;
- DPI scaling, disposal of images/fonts/timers, and cancellation of work when the form closes.

Do not change downloader, cookie, bridge or persistence behavior in the same commit as pure layout work unless the root cause requires it and has regression coverage.

### P4 — Reduce architectural coupling incrementally

1. Extract thumbnail HTTP fetching/caching/size limits into a small `ThumbnailService` with cancellation, safe URL policy and image disposal. Keep WinForms `PictureBox`/controls outside that service.
2. Extract pure presentation mapping into testable components: download row display state, toolbar action availability, progress-stage labels and error-to-action mapping.
3. Keep all Control access on the UI thread. Use asynchronous I/O for network/process work and marshal only final presentation updates back to the UI thread. Avoid sync-over-async waits on UI event paths. Microsoft guidance: [thread-safe WinForms control calls](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls).
4. Keep `MainForm` partials as a temporary organization aid, not the final boundary. Extract by responsibility only when there are tests for existing behavior; do not perform a large rewrite alongside visual redesign.
5. Define state transitions once so retry, cancel, pause/resume, merge and completion status cannot diverge between list, Details and notifications.

### P5 — Accessibility, automated UI and visual regression

- Add Windows UI automation for critical flows with [FlaUI](https://github.com/FlaUI/FlaUI) where stable selectors/accessibility names are available.
- Run [Accessibility Insights for Windows](https://accessibilityinsights.io/) on main window, Add URL, video preview, Preferences and error dialogs.
- Confirm high-DPI and scaling behavior against [WinForms high-DPI guidance](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms).
- Test mouse, keyboard-only navigation, visible focus, reduced motion where applicable, contrast, window resize and long/unicode input.
- Capture repeatable screenshots at fixed sizes/themes for visual comparison. UI automation complements, but does not replace, visual inspection.

### P6 — Release and platform decision

- Pass unit, integration, extension, clean-workspace, provenance, manual UI and packaging checks.
- Run real-site smoke tests only for accessible and authorized content; report the site, app/tool version and outcome. Do not claim that every public site will always work because extractors and site rules change.
- Current project targets `net10.0-windows` and WinForms, so it is a **Windows application**, not a browser-native or cross-platform app. A macOS/Linux/web edition requires a separate product/architecture plan: reusable non-UI download core plus a new supported UI host. Merely changing styles or adding an extension does not make WinForms run on macOS/Linux.

## Verification checklist

| Check | Command / method | Exit criterion |
| --- | --- | --- |
| Main regression suite | `powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1` | Build, package checks, .NET tests, integration tests and extension tests all pass. |
| Clean checkout | `powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1` | Project builds without ignored local binaries/cache and package checks behave as documented. |
| Release build | `dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64` | Zero errors; no unexplained new warnings. |
| Unit regression runner | `dotnet run --project FastDM.Tests -c Release` | All checks pass, including thumbnail URL/candidate tests. |
| Integration runner | `dotnet run --project FastDM.IntegrationTests -c Release` | Download, range, cancel/resume and child-process checks pass. |
| Extension | `npm test` in `FastDM.Extension` | Bridge, locale and UI state tests pass. |
| Thumbnail smoke | actual Windows app with reachable/unreachable YouTube/TikTok preview thumbnails | Successful image or explicit fallback; no permanent skeleton; no stuck UI. |
| UI layout | 520/700/900 widths; 100/125/150/200% DPI; light/dark; long names and paths | No clipped critical controls, no crash/flicker, focus and selection remain usable. |
| Accessibility | Accessibility Insights + keyboard-only walkthrough | Useful names/descriptions, visible focus, logical tab order and all actions keyboard reachable. |
| Package provenance | `scripts/Test-ToolProvenance.ps1 -ForRelease` | No unverified executable or missing redistribution notices. |

## Relevant source files

- [YtPickerForm.cs](../FastDM/UI/YtPickerForm.cs)
- [YtDlpMetadata.cs](../FastDM/YtDlp/YtDlpMetadata.cs)
- [MainForm.Layout.cs](../FastDM/UI/MainForm.Layout.cs)
- [MainForm.List.cs](../FastDM/UI/MainForm.List.cs)
- [MainForm.Downloads.cs](../FastDM/UI/MainForm.Downloads.cs)
- [Controls.cs](../FastDM/UI/Controls.cs)
- [Testing guide](TESTING.md)
- [Tool provenance and release gate](TOOLS.md)
- [Previous full audit report](CODEX_AUDIT_REPORT.md)

## Completion definition

The work is complete only when the code-level regression suite passes, the Windows UI has been manually tested at the matrix above, real thumbnail fallbacks have been exercised, async/cancellation behavior is checked, and the release provenance gate passes. A passing compile alone is not acceptance.
