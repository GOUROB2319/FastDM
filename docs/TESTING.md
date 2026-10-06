# Testing FastDM

Run these commands from the repository root in PowerShell.

## 1. Automated checks

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1
```

Expected final message: `Core build and extension tests passed.`

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
