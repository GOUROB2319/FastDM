# Testing FastDM

Run these commands from the repository root in PowerShell.

## 1. Automated checks

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-Project.ps1
```

Expected final message: `Core build and extension tests passed.`

This builds the Windows desktop application and runs the six Node tests for
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
