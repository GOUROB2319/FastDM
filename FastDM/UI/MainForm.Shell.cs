using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastDM
{
    // MainForm part: tray, closing, notifications, drag-drop, clipboard.
    public partial class MainForm
    {
        // ---------- সিস্টেম ট্রে ----------
        void BuildTray()
        {
            trayMenu =
                new ContextMenuStrip();

            trayMenu.Items.Add(
                "Open FastDM",
                null,
                (s, e) => RestoreFromTray());

            trayMenu.Items.Add(
                new ToolStripSeparator());

            trayMenu.Items.Add(
                "Pause all",
                null,
                (s, e) => PauseAll());

            trayMenu.Items.Add(
                "Resume all",
                null,
                (s, e) => ResumeAll());

            trayMenu.Items.Add(
                new ToolStripSeparator());

            trayMenu.Items.Add(
                "Exit",
                null,
                (s, e) => ExitApp());

            Icon? ico = null;

            try
            {
                ico =
                    Icon.ExtractAssociatedIcon(
                        Application.ExecutablePath);
            }
            catch (Exception exIgnored1) { AppLog.Write("MainForm.Shell (ignored): " + exIgnored1.Message); }

            tray =
                new NotifyIcon
                {
                    Icon =
                        ico ?? SystemIcons.Application,

                    Text = "FastDM",

                    ContextMenuStrip =
                        trayMenu,

                    Visible = true
                };

            tray.DoubleClick +=
                (s, e) => RestoreFromTray();

            tray.BalloonTipClicked +=
                (s, e) => RestoreFromTray();
        }

        void HideToTray(
            bool showTip)
        {
            Hide();

            if (showTip &&
                !trayTipShown)
            {
                trayTipShown = true;

                Notify(
                    "FastDM",
                    "Still running in the system tray. Click the icon to open it.");
            }
        }

        void RestoreFromTray()
        {
            if (!Visible)
                Show();

            if (WindowState ==
                FormWindowState.Minimized)
            {
                WindowState =
                    FormWindowState.Normal;
            }

            Activate();
        }

        void ExitApp()
        {
            exiting = true;
            Close();
        }

        void OnFormResize(
            object? sender,
            EventArgs e)
        {
            RequestToolbarFit();
            FitLastColumn();

            if (tray != null &&
                settings.MinimizeToTray &&
                WindowState ==
                    FormWindowState.Minimized)
            {
                HideToTray(false);
            }
        }

        void OnFormClosing(
            object? sender,
            FormClosingEventArgs e)
        {
            if (!exiting &&
                e.CloseReason ==
                    CloseReason.UserClosing)
            {
                if (settings.CloseToTray)
                {
                    e.Cancel = true;
                    HideToTray(true);
                    return;
                }

                bool busy =
                    items.Any(i =>
                        i.State ==
                            DlState.Downloading ||
                        i.State ==
                            DlState.Queued);

                if (busy)
                {
                    var r = ThemeDialog.Show(
                        this,
                        "Downloads are still running.\n\n" +
                        "Yes = keep running in the system tray\n" +
                        "No = exit (downloads will be paused)\n" +
                        "Cancel = stay",
                        "FastDM",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (r ==
                        DialogResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }

                    if (r ==
                        DialogResult.Yes)
                    {
                        e.Cancel = true;
                        HideToTray(true);
                        return;
                    }
                }
            }

            foreach (var it in items)
                it.Cts?.Cancel();

            SaveState();
        }

        void Cleanup()
        {
            SshHostKeys.FirstUseConfirmation = null;
            try
            {
                SystemEvents.UserPreferenceChanged -=
                    OnSysPrefChanged;
            }
            catch (Exception exIgnored2) { AppLog.Write("MainForm.Shell (ignored): " + exIgnored2.Message); }

            try
            {
                timer?.Stop();
                navTimer?.Stop();
                toolbarFitTimer?.Stop();
            }
            catch (Exception exIgnored3) { AppLog.Write("MainForm.Shell (ignored): " + exIgnored3.Message); }

            try
            {
                regWait?.Unregister(null);
                showEvent?.Dispose();
            }
            catch (Exception exIgnored4) { AppLog.Write("MainForm.Shell (ignored): " + exIgnored4.Message); }

            try
            {
                pipeCts?.Cancel();
            }
            catch (Exception exIgnored5) { AppLog.Write("MainForm.Shell (ignored): " + exIgnored5.Message); }

            StopBridge();

            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null!;
            }

            trayMenu?.Dispose();
        }

        // ---------- নোটিফিকেশন ----------
        void Notify(
            string title,
            string text,
            ToolTipIcon icon =
                ToolTipIcon.Info)
        {
            if (tray == null ||
                !settings.ShowNotifications)
                return;

            if (ActiveForm != null)
                return;

            try
            {
                if (title.Length > 60)
                    title =
                        title.Substring(
                            0,
                            60);

                if (text.Length > 240)
                    text =
                        text.Substring(
                            0,
                            240) +
                        "…";

                tray.ShowBalloonTip(
                    4000,
                    title,
                    text,
                    icon);
            }
            catch (Exception exIgnored6) { AppLog.Write("MainForm.Shell (ignored): " + exIgnored6.Message); }
        }

        void NotifyError(
            DownloadItem it)
        {
            if (!settings.NotifyFailed)
                return;

            if (
                (DateTime.UtcNow -
                 lastErrorNotify)
                    .TotalSeconds < 10)
                return;

            lastErrorNotify =
                DateTime.UtcNow;

            string message = it.FileName + "\n" + (it.Error ?? "");
            if (it.YtDlp != null)
            {
                var kind = YtDlpDiagnostics.Classify(it.Error);
                var description = YtDlpDiagnostics.Describe(kind, UpdateChecker.IsPackaged);
                message = it.FileName + "\n" + description.Summary + "\n" + description.NextStep + "\n" + (it.Error ?? "");
            }
            Notify("Download failed", AppLog.Redact(message), ToolTipIcon.Error);
        }

        // ---------- ড্র্যাগ-ড্রপ + ক্লিপবোর্ড ----------
        // A single dropped .txt / .csv / .m3u / .m3u8 file is a list of links.
        static string? DroppedListFile(DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files &&
                files.Length == 1)
            {
                string ext = Path.GetExtension(files[0]).ToLowerInvariant();

                if (ext == ".txt" || ext == ".csv" || ext == ".m3u" || ext == ".m3u8")
                    return files[0];
            }

            return null;
        }

        void OnDragEnter(
            object? s,
            DragEventArgs e)
        {
            bool accepted =
                e.Data != null &&
                (e.Data.GetDataPresent(DataFormats.Text) ||
                 DroppedListFile(e) != null);
            e.Effect = accepted ? DragDropEffects.Copy : DragDropEffects.None;
            if (accepted && s != this)
            {
                dragOverlay.Visible = true;
                dragOverlay.BringToFront();
            }
        }

        void OnDragDrop(
            object? s,
            DragEventArgs e)
        {
            dragOverlay.Visible = false;
            string? listFile = DroppedListFile(e);

            if (listFile != null)
            {
                ImportListFile(listFile);
                return;
            }

            if (e.Data?.GetData(
                    DataFormats.Text)
                is string t)
            {
                ShowAddDialog(
                    t.Trim());
            }
        }

        void OnDragLeave(object? s, EventArgs e)
        {
            dragOverlay.Visible = false;
        }

        void CheckClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                    return;

                string t =
                    Clipboard.GetText().Trim();

                if (t == lastClip)
                    return;

                lastClip = t;

                if (!Uri.TryCreate(
                        t,
                        UriKind.Absolute,
                        out var u) ||
                    (u.Scheme !=
                        Uri.UriSchemeHttp &&
                     u.Scheme !=
                        Uri.UriSchemeHttps))
                {
                    return;
                }

                string ext =
                    Path.GetExtension(
                        u.AbsolutePath)
                        .ToLowerInvariant();

                if (!FileExts.Contains(ext))
                    return;

                BeginInvoke(
                    new Action(
                        () =>
                            ShowAddDialog(t)));
            }
            catch (Exception exIgnored7) { AppLog.Write("MainForm.Shell (ignored): " + exIgnored7.Message); }
        }

        void ShowStartupNotice()
        {
            if (string.IsNullOrEmpty(startupNotice))
                return;

            string text = startupNotice;
            startupNotice = null;

            ThemeDialog.Show(
                this,
                text,
                "FastDM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
