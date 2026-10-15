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
    // MainForm part: tray, sidebar, closing, notifications, drag-drop, clipboard.
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
            catch { }

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
                    var r = MessageBox.Show(
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

        void UpdateSideCounts()
        {
            for (int i = 0;
                 i < sideCounts.Length;
                 i++)
            {
                int idx = i;

                sideCounts[i] =
                    idx == DeletedFilter
                        ? deleted.Count
                        : items.Count(
                            x => Match(idx, x));
            }
        }

        // ---------- Sidebar ----------
        void ToggleSidebar()
        {
            settings.SidebarOpen =
                !settings.SidebarOpen;

            UpdateSideCounts();

            navTarget =
                settings.SidebarOpen
                    ? 200
                    : 0;

            if (settings.SidebarOpen)
                sidebar.Visible = true;

            navTimer.Start();

            SaveState();
        }

        void StepSidebar()
        {
            const int step = 40;

            int w =
                sidebar.Width;

            if (w < navTarget)
            {
                w =
                    Math.Min(
                        navTarget,
                        w + step);
            }
            else if (w > navTarget)
            {
                w =
                    Math.Max(
                        navTarget,
                        w - step);
            }

            sidebar.Width = w;

            FitLastColumn();

            if (w == navTarget)
            {
                navTimer.Stop();

                if (w == 0)
                    sidebar.Visible = false;
                else
                    sidebar.Invalidate();
            }
        }

        void Cleanup()
        {
            try
            {
                SystemEvents.UserPreferenceChanged -=
                    OnSysPrefChanged;
            }
            catch { }

            try
            {
                timer?.Stop();
                navTimer?.Stop();
                toolbarFitTimer?.Stop();
            }
            catch { }

            try
            {
                regWait?.Unregister(null);
                showEvent?.Dispose();
            }
            catch { }

            try
            {
                pipeCts?.Cancel();
            }
            catch { }

            StopBridge();

            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
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
            catch { }
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

            Notify(
                "Download failed",
                it.FileName +
                "\n" +
                (it.Error ?? ""),
                ToolTipIcon.Error);
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
            e.Effect =
                e.Data != null &&
                (e.Data.GetDataPresent(DataFormats.Text) ||
                 DroppedListFile(e) != null)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
        }

        void OnDragDrop(
            object? s,
            DragEventArgs e)
        {
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
            catch { }
        }

        void ShowStartupNotice()
        {
            if (string.IsNullOrEmpty(startupNotice))
                return;

            string text = startupNotice;
            startupNotice = null;

            MessageBox.Show(
                this,
                text,
                "FastDM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
