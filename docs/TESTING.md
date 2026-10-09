# Testing FastDM

Run these commands from the repository root in PowerShell.

## 1. Automated checks

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1
```

Expected final message: `Core build, bridge tests and extension tests passed.`

What it runs:

- `dotnet build` of the desktop app.
- `FastDM.Tests`: about 100 checks on the extension bridge (pairing, token and extension-ID binding, cookie isolation, allowed link types, the `fastdm://` protocol) against a real local server. Close other copies of FastDM first if the test says the server cannot start (it needs one free port from 17432 to 17436).
- `npm test` in `FastDM.Extension`: bridge client tests plus a check that the English and Bangla texts, the manifest and the HTML use the same keys.

The same checks run on GitHub for every push and pull request to `master` and `test` (Actions tab, workflow **CI**).

This builds the Windows desktop application and runs the Node tests for
the browser-extension bridge.

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

## 3f. YouTube cookies (yt-dlp) and text encoding

| Check | Expected |
| --- | --- |
| Text encoding | Toolbar, Add dialog and status bar show `…`, `—`, `→` and the toolbar icons correctly, with no garbled characters. |
| Preferences → Advanced → Video sites | A "Use cookies from browser" box with None / Microsoft Edge / Google Chrome / Mozilla Firefox / Brave. Default is None. |
| Save, close, reopen Preferences | The chosen browser is still selected. Reset puts it back to None. |
| YouTube link, cookies None | If YouTube asks you to sign in, the error box explains to choose a browser in Preferences. |
| YouTube link, Edge selected, Edge fully closed | The quality picker opens (no sign-in error). If Edge blocks cookie access, the error box suggests Firefox. |
| Same, Firefox selected | The quality picker opens when you are logged in to YouTube in Firefox. |
| Hand-edit `state.json`: `"YtCookieBrowser": "--exec calc"` | The value is ignored (treated as None); nothing extra is passed to yt-dlp. |

## 3g. Extension origin binding

| Check | Expected |
| --- | --- |
| Pair Edge, then add a link | Works as before. |
| Update from a build that was already paired (before this change) | The browser keeps working with no re-pair, and it is bound to its extension ID on first use. |
| Install the extension a second time from another folder (a different extension ID) and press Connect | FastDM asks for approval and shows the new ID. Choose No: pressing Connect again within a minute shows "denied" with no new dialog. |
| After saying No, wait about a minute and press Connect again | The dialog appears again. |
| Link from the second extension without pairing it | Rejected (401). Only the paired ID can send links. |
| Preferences → Browser Integration → Forget paired browsers | Every browser needs to Connect again. |

## 4. Package the extension

```powershell
Set-Location FastDM.Extension
powershell -ExecutionPolicy Bypass -File tools/pack.ps1
```

The upload-ready archive is created in
`FastDM.Extension/dist/FastDM-Extension-0.1.0.zip`.

## 5. Build the MSIX package (optional)

Install Visual Studio 2022 with the **MSIX Packaging Tools** component. Then
open `FastDM.slnx`, choose `Debug | x64`, and use **Build Solution**. This
requires Visual Studio's Desktop Bridge targets; `dotnet build FastDM.slnx`
alone is not sufficient.
