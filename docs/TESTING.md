# Testing FastDM

Run these commands from the repository root in PowerShell.

## Latest automated verification (2026-10-23)

The latest local run passed `scripts/Test-Project.ps1`: desktop Debug build (0
warnings/errors), package/version checks, 407 .NET regression checks, 30 local
HTTP/process integration checks, and 20 extension tests. The standalone Release
desktop build also passed with 0 warnings/errors. The explicitly selected Node
bridge/localization tests passed 18/18.

To check reproducibility without ignored local tools or build outputs, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1
```

This creates and removes an isolated temporary source copy, runs the Release
build and all automated suites, validates package metadata, and builds an
unsigned MSIX when Visual Studio MSBuild is available. The 2026-10-23 run
generated `FastDM.Package_1.0.13.0_x64.msixbundle` and the extension ZIP. MSIX
generation does not verify signing, installation, Store certification, or
third-party tool redistribution rights. Local FFmpeg/ffprobe provenance is
still unverified and remains a release blocker; see [`CODEX_AUDIT_REPORT.md`](CODEX_AUDIT_REPORT.md).

## Fourth-pass manual checks (2026-10-23)

Automated tests cannot verify these interactive layout and external-service behaviors. Run them on Windows with the app open:

- Resize to 1359 px with the sidebar closed; Schedule and After downloads must retain icon and text, and the overflow chevron must appear only after lower-priority actions collapse.
- Toggle the sidebar repeatedly; all list columns must fill the viewport without a right-side gap. Check status-bar labels in both themes.
- Run four downloads and watch the sidebar, list rows, header, and details panel for blinking or unnecessary full redraws.
- Open video, audio, and playlist quality dropdowns near all screen edges; each list must remain inside the working area and open upward when needed.
- Preview a YouTube thumbnail and a Bilibili thumbnail; confirm the fallback placeholder appears on failure without blocking the dialog.
- Import a `.txt`/`.csv` list and confirm the default destination uses the sanitized file name. Preview a playlist and confirm its destination uses the sanitized playlist title; toggle each folder option and check the displayed final path.
- Open an FTP folder with at least three nested levels; verify the complete tree, initial expansion, checkboxes and partial-parent labels, extension filter, selected-size total, expand/collapse, and cancellation while scanning.

Live YouTube/Bilibili extraction and thumbnail availability depend on upstream services and were not part of the automated verification. Use only authorized test content and user-selected cookie sources; do not share session cookies.

## 1. Automated checks

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1
```

Expected final message: `Desktop build, package checks, regression tests and extension tests passed.`

What it runs:

- `dotnet build` of the desktop app.
- `scripts/Test-PackageManifest.ps1`: package/app version consistency, manifest asset sources and copies of any locally installed optional tools.
- `FastDM.Tests`: dependency-free checks for bridge pairing and limits, URL credentials and log redaction, cookie scope, yt-dlp proxy/cookie selection, range validation, protocol handling, and existing app helpers. Bridge tests use a local server; close other FastDM copies if a test cannot bind a port from 17432 to 17436.
- `FastDM.IntegrationTests`: loopback-server tests for exact range bytes, redirects, temporary retries, server errors, bad `Content-Range`, zero-byte response, per-download cookie isolation, and cancel/resume; it also checks child-process Unicode output and cancellation.
- `npm test` in `FastDM.Extension`: bridge client tests plus a check that the English and Bangla texts, the manifest and the HTML use the same keys.

The same checks run on GitHub for every push and pull request to `master` and `test` (Actions tab, workflow **CI**).

This builds the Windows desktop application and runs package, .NET, local HTTP
integration and Node extension checks.

## 2. Test the desktop app

```powershell
dotnet run --project FastDM/FastDM.csproj -c Debug -p:Platform=x64
```

In FastDM, open **Settings** and ensure **Allow the browser extension to
connect** is checked. Keep FastDM running while testing the extension.

## 3. Load the extension in Edge or Chrome

1. Open `edge://extensions` (or `chrome://extensions`).
2. Enable **Developer mode**.
3. Select **Load unpacked**.
4. Choose the `FastDM.Extension` folder itself, the folder containing
   `manifest.json`.
5. Pin the FastDM extension, open its popup, then select **Connect**.
6. In the FastDM confirmation dialog, select **Yes**. The popup should show
   that it is connected.
7. Right-click a normal download link and choose **Download with FastDM**.
   FastDM should open its Add dialog. Confirm the dialog and verify the item
   appears in the download list.

Also test **Open FastDM** in the popup after closing the app. The `fastdm://`
handler should open FastDM and show the Add dialog after a link is sent.

## 3b. Phase 2 checks (context menu, headers, cookies)

After reloading the extension, right-click a link: there should be one
**FastDM** entry with a submenu.

| Where you right-click | Submenu items | Expected result |
| --- | --- | --- |
| A normal file link | **Download with FastDM** | The download starts at once in the default folder, no dialog. |
| A normal file link | **Open in FastDM** | The Add dialog opens with the link filled in. |
| A video or audio element | **Download media…** / **Open media…** | Same two behaviours for the media address. |
| The page background | **Send page link to FastDM** | The Add dialog opens with the page address. |

Notes for the tester:

- If FastDM is closed, both link items start the app through `fastdm://`
  and the Add dialog opens (the protocol never starts a download by itself).
- Folder links, video pages and yt-dlp sites always show their own window
  (folder tree, quality picker), even with **Download**.
- **Referer / User-Agent:** download a file from a site that blocks direct
  links (hotlink protection). It should work from the menu and fail when the
  same link is pasted into the Add dialog by hand.
- **Cookies:** open the extension **Settings**, turn on **Login cookies** and
  accept the browser prompt. Then download a file that needs a signed-in
  session. Turn it off again and confirm the same file fails. Cookies are kept
  in memory only; after restarting FastDM a resumed item has no cookies.
- Negative check: send a request to `http://127.0.0.1:17432/v1/add` from a
  normal web page (for example from the browser console). It must be refused.

## 3c. Phase 3 checks (playlists for VLC)

Open a folder window (right-click a directory-listing link, or add a folder
link in the Add dialog). At the bottom you now have two playlist options:

| Option | What it does |
| --- | --- |
| **Save stream playlist…** | Writes a `.m3u8` with the server links of the ticked video/audio files. VLC plays them straight from the server, no download. Files are sorted naturally (Ep 2 before Ep 10). |
| **Also create a local playlist (.m3u8)…** (checkbox) | When you press **Download**, a `<folder name>.m3u8` with relative paths is written into the download folder. It plays offline once the files have arrived. |

Browser extension: right-click a link or page whose address ends with `/`
(for example `https://server/movies/`). The **FastDM** submenu has
**Create playlist with FastDM** (link) or **Create playlist from this folder**
(page). The folder window opens directly with the playlist button as the main action.

Checks:

- Open the saved stream playlist in VLC. Episodes must play in order.
- For an FTP/SFTP server that needs a login, open the `.m3u8` in Notepad:
  there must be **no** username or password in any line. VLC asks for them itself.
- Non-media files (`.srt`, `.nfo`, images) must not be in the playlist; the status
  line says how many were skipped.
- Close FastDM, then use the menu again: `fastdm://playlist` opens the app and asks
  you to confirm before it scans the folder.

## 3d. Preferences page (Step 1a)

Open **Settings** (gear icon or tray menu). The new **Preferences** window has a
list on the left (General, Downloads, Browser Integration, Network, Antivirus,
Notifications, Advanced). Clicking a name scrolls to that section; scrolling
moves the highlight. Resize the window and test at 100%, 125% and 150% screen scaling.

Check each setting does something:

| Setting | How to check |
| --- | --- |
| Suggest folders by file type / URL | Add a `.pdf` link with the default folder: it lands in `Documents` (and `<site name>`). Pick another folder by hand: no sub-folder is added. |
| Compact view | Rows become shorter at once after Save. |
| Auto-remove deleted files | Delete a finished file in Explorer: the row disappears within ~30 seconds. |
| Auto-remove completed | A download vanishes from the list when it finishes; the file stays. |
| Auto-retry | Start a download, turn the network off for a few seconds: status goes to retry (5 s, 10 s, 15 s) instead of failing at once. |
| Do not download web pages | Add a link that returns an HTML page (a normal web page address): it is skipped and the status bar says so. |
| Server time | Finished file's *Modified* date equals the server's `Last-Modified`. |
| Mark downloaded files | File Properties shows the "This file came from another computer" Unblock box (NTFS only). |
| Max urls in batch | Paste more links than the limit: a message says only the first N are added. |
| Notifications (added / completed / failed) | Minimize to tray, then add, finish and break a download. Each switch controls its own balloon. |
| Antivirus | Choose *Windows Defender*, tick the automatic scan, finish a download: a Defender scan starts (see `MpCmdRun.exe` in Task Manager). |
| Launch external application | Path `notepad.exe`, arguments `%path%`: finished text files open in Notepad. |
| Delete button action | Remove only / Delete files / Always ask behave as named when you press Remove on a finished download. |
| File exists reaction | Download the same file twice: Rename gives `name (1)`, Overwrite replaces it, Always ask shows a Yes/No box. |
| Enable logging + Open log folder | A `fastdm-<date>.log` appears under `%LocalAppData%\FastDM\logs` with Start/Completed/Error lines. |
| Reset | Everything returns to defaults; paired browsers and the download list stay. |

Not in this step: Launch at startup, Language, UI style, Zoom, Low/Medium/High
traffic presets, browser download interception and BitTorrent.

## 3e. Traffic modes and Launch at startup (Step 1b)

**Mode button.** The old *Speed* button in the toolbar is now **Mode: High / Medium / Low**.
Its menu lists the three modes with their speed and simultaneous downloads, and
**Edit modes…** opens Preferences → **Traffic Limits**. The status bar shows
`Mode: Medium · 2 MB/s`.

| Check | How |
| --- | --- |
| Speed per mode | Choose **Low** (default 256 KB/s): the total speed of all downloads stays near that. Choose **High**: unlimited. |
| Simultaneous downloads | Queue 6 files in **Low** (2 at a time) and in **High** (4 at a time): *Active* in the status bar matches. |
| Total connections | In Preferences set **Medium** to 10 connections, 5 per server, add 3 large files from one site: never more than 5 connections to that site and 10 in total (check with Resource Monitor → Network). |
| Live change | Switch modes while downloading: the new limits apply at once, running segments finish. |
| Old settings kept | After upgrading, **High** holds your previous speed limit, connections and simultaneous downloads, and the mode starts as High. |
| Proxy | Preferences → Network → **Proxy…** opens the smaller Proxy window (no speed field any more). |
| Pause slow downloads | Turn it on with *0 KB/s for 1 minute*, queue more downloads than the simultaneous limit, then pull the network cable for one download only (or use a stalled link): after about a minute it goes to the back of the queue and a waiting one starts. With an empty queue nothing is paused. |

**Launch at startup (minimized)** — Preferences → General → *Startup*.

| Build | Expected |
| --- | --- |
| Portable | Tick it and Save: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` has a `FastDM` value ending in `--minimized`. Sign out and in: FastDM starts hidden in the tray. Untick: the value is removed. |
| Store (MSIX) | Tick it and Save: Task Manager → *Startup apps* shows **FastDM** as *Enabled*. Restart Windows: FastDM starts hidden in the tray. If you switch it off in Task Manager, the Preferences box shows a note and the option cannot be turned back on from the app (Windows rule); enable it in Task Manager first. |

The Store check needs the signed MSIX installed; a plain Visual Studio *Debug* run is "portable".

## 3f. YouTube cookie selection and diagnosis

| Check | Expected |
| --- | --- |
| Preferences → Advanced → Video sites | Cookie source shows None, a selected browser, or cookies.txt. A missing or malformed file is called out before a request runs. |
| Select a browser, save, close and reopen Preferences | The browser remains selected. Reset changes it to None. Browser cookies are read only when the user selects a browser source. |
| Select a valid Netscape cookies.txt file | It becomes the active source and takes priority over the browser. Clear the path to return to the browser selection. Never attach or share this file. |
| Select a missing, empty, or malformed cookies.txt file | The source indicator and error identify the file problem without printing its path or contents. |
| Reproduce Image #1 with Chrome/Edge closed, if the local browser profile still rejects access | The error identifies a browser cookie-database copy/lock problem and suggests closing background browser processes or choosing another supported source. FastDM must not weaken browser cookie protection or kill the browser. |
| Reproduce Image #2 with a YouTube request that requires verification | The error identifies a browser verification/sign-in requirement and offers the optional user-selected cookie source. FastDM must not bypass CAPTCHA or authentication. |
| Use a malformed/private/unavailable link or a network failure | The error does not incorrectly recommend cookies when the classified failure is format, access, DNS, rate limit, proxy, or network related. |
| Copy technical details from the error dialog | Query values, credentials, cookies, proxy secrets, and private paths are redacted. No diagnostic is sent to a remote service. |

Automated tests do not read a real browser profile or cookies.txt file. Use a disposable test profile if manually testing cookie access; never add its cookie file to the repository or logs.

## 3g. Extension origin binding

| Check | Expected |
| --- | --- |
| Pair Edge, then add a link | Works as before. |
| Update from a build that was already paired (before this change) | The browser keeps working with no re-pair, and it is bound to its extension ID on first use. |
| Install the extension a second time from another folder (a different extension ID) and press Connect | FastDM asks for approval and shows the new ID. Choose No: pressing Connect again within a minute shows "denied" with no new dialog. |
| After saying No, wait about a minute and press Connect again | The dialog appears again. |
| Link from the second extension without pairing it | Rejected (401). Only the paired ID can send links. |
| Preferences → Browser Integration → Forget paired browsers | Every browser needs to Connect again. |

## 3h. Saved list recovery (state.json)

Your download list and settings live in `%APPDATA%\FastDM` (press Win+R, type that, Enter). Close FastDM before editing files there.

| Check | Expected |
| --- | --- |
| Normal restart | The list and settings are unchanged. No warning. After FastDM has saved once (add a download, or close the app) you see `state.json` and `state.json.bak`. |
| Open `state.json` in Notepad, type junk at the end, save, start FastDM | A warning says the list was **restored from the latest automatic backup**. The list is back. A file `state.json.damaged-<time>` exists and still contains your junk. |
| Delete `state.json.bak` and `state.json.bak2`, damage `state.json` again, start FastDM | A warning says it **started with an empty list**. `state.json.damaged-<time>` still holds the old list, so nothing is lost (you can copy it back). |
| Make `state.json` empty (0 bytes), start FastDM | Same warning as above. |
| Damage `state.json` several more times | Only the 5 newest `state.json.damaged-*` files are kept. |
| Put `"SchemaVersion": 99,` as the first line inside the `{ }` of `state.json`, start FastDM | A warning says the settings come from a newer version; `state.json.from-v99` is created once; FastDM still works. |
| Add a download, wait more than 15 minutes with FastDM open | `state.json.bak` becomes newer and the previous one becomes `state.json.bak2`. |

## 3i. Video preview, download stages, and selection recovery

| Check | Expected |
| --- | --- |
| Add a YouTube watch URL or short URL, including one with `&list=...` | One single-video preview opens immediately. Title, channel/source, duration, thumbnail, format choices, estimated or unknown size, file name, and save folder fill in asynchronously. |
| Add a YouTube playlist URL without a video ID | The same preview window lists available entries, reports the listed/reported count or unknown total, and lets you select entries and quality. Only the configured maximum URL count can be selected. |
| Cancel while metadata is loading | The dialog closes promptly and cancels the yt-dlp child process. No download item is added. |
| Choose **Download Later** | The selected item or playlist entries are added paused. **Start download** queues them immediately. |
| Add the same YouTube video twice through browser/clipboard events | Only one active or queued job is added; a watch URL with a playlist parameter remains one video. |
| Preview a format with known size and one without size metadata | The dialog labels sizes as estimates and says “unknown” when no size estimate is available; it never presents an estimate as exact. |
| Run without FFmpeg installed | Formats requiring a video/audio merge and audio conversion are unavailable. A compatible single-stream format remains selectable when yt-dlp reports one. |
| Download a format requiring FFmpeg | During merge/post-processing, the list and Details panel show that stage; transfer speed and ETA are blank. Completion appears only after a non-empty output file is found. |
| Trigger a 429 response with and without a `Retry-After` header | yt-dlp's attempts remain bounded. FastDM does not retry a rate limit again without a server delay; with a delay it waits and performs at most one outer retry. |
| Select a video, then remove it or clear Deleted history | Details clear immediately and never remain bound to the removed item. |
| Run an updater request while a video process is active | The update waits until active yt-dlp children finish; new yt-dlp jobs wait until the update releases the process gate. |
| Resize the preview at 100%, 125%, 150%, and 200% scaling; use Tab, Space, Enter, and Escape | Controls remain visible, selectable, and keyboard operable. Cancel works during metadata loading. |

A live YouTube check is optional and network-dependent. Use a public video without account cookies first. If the site requests verification, complete it in the browser and only then opt in to a supported signed-in browser or a user-exported cookies.txt file. Do not attempt to bypass verification, DRM, or access restrictions. Automated tests use fixtures and local processes only.
### Importing multiple links from a text file (batch workflow)

| Check | Expected |
| --- | --- |
| Add URL -> **Open text file...** and pick a .txt with about 10 YouTube links (some with `&list=...`), 2 folder links and a few lines of comments (`# ...`) | The **Import links** window opens. Comments and blank lines are skipped. Each line shows its type. YouTube links with a playlist part are listed as one video with a note. |
| Same file, but add a `youtube.com/playlist?...` line and a line of plain text | Both are listed greyed out and cannot be ticked (playlist / not a link). |
| Repeat one link twice | The repeat is listed, not ticked, with the note "same link as an earlier line". |
| Pick quality **Up to 720p** and press **Start downloading** | One download per ticked video (never more than the number of lines). Batch entries queue without an individual preview and are named after the video title when each one finishes. Adding one video URL directly through **Add URL** uses the unified asynchronous preview instead. |
| A folder link with "Download whole folders automatically" ticked | The folder is scanned (bottom bar shows progress) and every file goes into `<Save to>\<folder name>`, keeping its sub-folders. No window per folder. |
| Same, with that box unticked | The usual "Download Folder" window opens for each folder. |
| Pick **Audio only (M4A)** | The downloads become .m4a files. |
| Drag a .txt file onto the FastDM window | The same Import links window opens. |
| Paste 3 normal file links (no video sites) in Add URL | Works as before (no import window). |
| Remove a running title-named download | Its hidden `.fastdm-xxxxxxxx` folder inside the save folder disappears. |
| Pause a title-named download, then resume it | It continues (the half-finished file is kept in the hidden folder). |

### Deleted list

| Check | Expected |
| --- | --- |
| Look at the left menu | A new **Deleted** entry sits under Errors, with a count. |
| Remove a finished download and choose **Yes** (list only) | It leaves All Downloads and appears under **Deleted** with the reason "Removed from the list". The count goes up. |
| Remove a finished download and choose **No** (also delete the file) | It appears under Deleted as "Removed, file deleted from disk"; the file is gone from the disk. |
| Remove a download that is still running | It appears under Deleted as "Removed before it finished". |
| Delete a finished file in Windows Explorer, wait up to 30 seconds | The download leaves the list by itself and appears under Deleted as "File deleted from disk". |
| Eject a USB drive that holds finished downloads, wait a minute | Those downloads stay in the list (an unplugged drive is not "deleted"). Plug it back in and all is as before. |
| Open **Deleted** | The last column is called **Deleted** and shows when. The bottom panel's Status says "Deleted: ...". Resume / Pause do nothing here. |
| Right-click in **Deleted** | The menu offers Open Folder, Copy URL, **Download again**, **Remove from history**, **Clear history**. |
| **Download again** (menu, or double-click) | After a question, a fresh download starts from the same link and folder, and the entry leaves Deleted. |
| Press **Delete**, or the toolbar **Remove**, in Deleted | It asks to remove the entries from the history; files on the disk are not touched. **Clear history** empties the list. |
| Press **Ctrl+A** in Deleted | Every entry is selected. |
| Close and start FastDM again | The Deleted list is still there. |
| Update from the previous version | Preferences > Downloads shows "Move downloads whose file was deleted from the disk to the Deleted list" already ticked. |

## 4. Package the extension

```powershell
Set-Location FastDM.Extension
powershell -ExecutionPolicy Bypass -File tools/pack.ps1
```

The upload-ready archive is created in
`FastDM.Extension/dist/FastDM-Extension-0.1.0.zip`.
If an archive with that name already exists, the packer chooses the next unused
`-1`, `-2`, and so on suffix instead of overwriting the existing archive.

## 5. Build the MSIX package (optional)

Install Visual Studio with the **MSIX Packaging Tools** component. Then open
`FastDM.slnx`, choose `Debug | x64`, and use **Build Solution**. This requires
Visual Studio's Desktop Bridge targets; `dotnet build FastDM.slnx` may not
locate them on its own. Use that Visual Studio installation's MSBuild when
needed.


## 6. Check package metadata and local tools

The main test script runs the MSIX manifest/version/asset check after the app
build. To run it directly:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-PackageManifest.ps1
```

The check reports optional local tools and confirms that any present under
`FastDM/Tools/` were copied to the app build output. A clean checkout has no
local tool binaries and should still build; see [`docs/TOOLS.md`](TOOLS.md).

## 7. Verify the current source in a clean temporary workspace

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1
```

This copies tracked and non-ignored source files to a uniquely named directory
under the operating system's temporary folder, leaving ignored local tools,
build outputs, and caches behind. It builds the desktop app, checks package
metadata, builds the MSIX package when Visual Studio MSBuild is available,
runs the .NET, loopback integration, and extension tests, and packs the
extension there. It then removes only that temporary copy.

## 8. Additional automated checks

```powershell
dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64
dotnet run --project FastDM.Tests -c Release
dotnet run --project FastDM.IntegrationTests -c Release
node --test FastDM.Extension/tests/bridge.test.mjs FastDM.Extension/tests/locales.test.mjs
node --test FastDM.Extension/tests/ui-state.test.mjs
powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1
git diff --check
```

The integration suite uses a loopback HTTP server and local child processes. It covers exact range bytes, resume/cancel, redirect, zero-byte and non-range responses, scoped cookies, directory listing/navigation bounds and concurrency caps. It does not contact a real video site, use a browser profile, or connect to FTP/SFTP.

Before creating a distribution artifact, run the stricter gate:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1 -ForRelease
```

It currently fails for the ignored local FFmpeg/ffprobe build and DLLs because their upstream provenance and complete notices/source are unknown. A normal source checkout has no local tools and builds with video/merge features unavailable.

## 9. Manual UI and remote-server matrix

These checks require a Windows interactive desktop and representative servers. Automated build/test results do not prove them:

| Area | Manual checks |
| --- | --- |
| Main toolbar and table | Resize 520/700/900 px with the sidebar open and closed, then maximize. Verify there is no horizontal scrolling; all seven columns return at normal width; narrow layouts retain Name/Size/Progress and expose hidden values through row tooltips and Details. Check sorting, selection, compact view, progress rendering, and long Unicode names. |
| Preferences | Resize from 900×640 to the 780×520 minimum and use a short display height. Verify no horizontal scrolling; path fields, Browse/Clear, checks, wrapped descriptions, traffic limits, Save/Cancel and section navigation remain accessible. |
| Batch Import | Check initial, minimum-width, wider and minimum-height sizes. Verify Video quality and Save to labels stay beside their controls; Browse remains visible; the list columns fit; action buttons and selection count remain available. |
| Details Panel | Tab to the panel; use Up/Down, wheel, scrollbar track and thumb, Left/Right, Enter and Space. Check focus cues, screen-reader names/descriptions, long path/URL/error access, right-click copy, and retained speed graph/connection information. |
| Extension popup/options | Check loading, connected, unpaired, disconnected and error states in English and Bangla. Disconnected shows one Open FastDM button; connected tasks show loading, empty and error feedback. Check pause/resume disabled/error behavior and centered options width at narrow and wide browser sizes. |
| DPI, theme and access | 100/125/150/200% DPI and text scaling; light/dark/system theme; keyboard-only focus order, Enter/Escape, screen-reader announcements, high contrast, long English/Bangla text and small display-height clipping. |
| Add URL and video preview | Paste valid links, blank lines and an invalid link; verify the live detected-link count, line-specific inline error and field focus after validation. Start a video preview and verify the themed progress/skeleton, disabled format controls while loading, and Retry after a failed preview. |
| Remote folder picker | Navigate into nested folders and back using breadcrumbs; verify shell folder/file icons, selected-size states (known, unknown and calculating), and selection counts. |
| Dialog accessibility | In Add URL, video preview and remote-folder dialogs, tab through controls in visual order, confirm controls have useful accessible names/descriptions, and verify validation/status updates are announced by the configured screen reader. |
| Merge states | With a reviewed FFmpeg build, select separate video/audio streams. Verify row, Details and graph show merging with no stale speed/ETA; test success, missing FFmpeg, disk-full/output-lock, cancellation and retry; confirm completion only after non-empty output exists. |
| FTP/SFTP browse | Open an FTP and SFTP directory, navigate into a child, use Back/Up, select a file, then explicitly download a folder. Test spaces, Unicode, large directories, permission denial, credentials, symlinks, cancellation and changed SFTP fingerprints. Confirm that pasting a folder does not immediately queue all descendants. |
| HTTP indexes | Test Apache/nginx/h5ai supported index pages and an ordinary HTML page with directory-looking links; verify unsupported pages are rejected. Test redirects, encoded names, traversal-looking links and 1,000-entry cap. |
| Four-way queue | Queue four slow independent items at High (cap 4), verify four active; set cap 1 and verify serialization. Pause/resume/cancel/retry and scheduler boundaries should release slots without duplicates/deadlock. |
| Video and cookies | None mode on public video, selected Brave missing/locked DB, valid/missing/malformed cookies file, bot-check/login, removed/private video, rate limit and network failure. No access-control bypass; ensure explicit Retry without cookies is user-triggered. |
| Store package | Follow [`STORE_RELEASE_CHECKLIST.md`](STORE_RELEASE_CHECKLIST.md), including install/upgrade/uninstall, identity, privacy and Partner Center listing review. |

Record each manual outcome and Windows/tool version before release. The Add URL, preview, folder-picker, dialog accessibility, and second-pass responsive UI checks above were not executed in the automated run. Popup/options state behavior has automated logic tests, but no live browser rendering check was run. No live browser cookie or FTP/SFTP server matrix was executed for this audit.

## 10. Current verification record - 2026-10-19

These results were recorded after the current source changes. The initial repository state was clean on `master` at `62f2c5a`; ignored local tools were preserved.

| Command/check | Verified result |
| --- | --- |
| `dotnet build FastDM/FastDM.csproj -c Release -p:Platform=x64` | **PASS**; 0 warnings, 0 errors. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1` | **PASS**; Debug app build, manifest/version/tool-copy checks, 345 .NET regression tests, 30 local integration checks, and 20 extension tests. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-CleanWorkspace.ps1` | **PASS**; source-only Release build, package checks, MSIX bundle, .NET and integration suites, extension tests and ZIP. The temporary tree is removed by the script. |
| Clean-workspace MSIX | **PASS as unsigned package generation**; `FastDM.Package_1.0.13.0_x64.msixbundle`, 73,391,291 bytes. Not installed or submitted. |
| Clean-workspace extension package | **PASS**; `FastDM-Extension-0.1.0.zip`, 34,467 bytes. |
| `node --test tests/ui-state.test.mjs` from `FastDM.Extension` | **PASS**; 2 tests. |
| NuGet vulnerability audit: `FastDM`, `FastDM.Tests`, `FastDM.IntegrationTests` | **PASS**; no vulnerable packages were reported for any project by `dotnet list ... package --vulnerable --include-transitive`. |
| `powershell -ExecutionPolicy Bypass -File scripts/Test-ToolProvenance.ps1 -ForRelease` | **BLOCKED as intended**; exit 1 because the nine FFmpeg/ffprobe executables and DLLs listed in [`CODEX_AUDIT_REPORT.md`](CODEX_AUDIT_REPORT.md) have unverified upstream provenance/redistribution terms. Their hashes and reported versions match the local lock. |
| `git diff --check` | **PASS after source and documentation edits**; no whitespace errors. Git emitted line-ending conversion notices only. |

The integration suite includes a changed-ETag resume case: it verifies `If-Range` is sent and the changed representation is not finalized. It also covers bounded child-process lines, oversized media manifests, HLS/DASH limits, DTD rejection, range integrity, cookies, directory-listing bounds, concurrency and cancel/resume. Automated tests do not replace the live browser, accessibility, FTP/SFTP, YouTube, merge-with-reviewed-FFmpeg, signing, installation or Store checks in the manual matrix above.
