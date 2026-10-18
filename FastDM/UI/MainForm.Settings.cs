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
    // MainForm part: schedule, after-all actions, preferences, traffic mode, startup and theme.
    public partial class MainForm
    {
        // ---------- শিডিউলার / "সব শেষ হলে" / ডিটেইলস ----------
        void ShowSchedule()
        {
            using var f =
                new ScheduleForm(settings);

            Theme.Apply(f);
            f.ShowDialog(this);

            SaveState();

            lastAllowed = null;
            MarkChanged();
        }

        void StartNowSelected()
        {
            if (filter == DeletedFilter)
                return;

            foreach (var it in Sel())
            {
                if (it.State ==
                        DlState.Paused ||
                    it.State ==
                        DlState.Error ||
                    it.State ==
                        DlState.Queued)
                {
                    it.IgnoreSchedule = true;
                    it.PausedBySchedule = false;
                    it.State = DlState.Queued;
                    it.Error = null;
                }
            }

            MarkChanged();
        }

        void BuildDoneMenu()
        {
            bool icons =
                Icons.Available;

            doneMenu =
                new ToolStripDropDownButton(
                    icons
                        ? "When done"
                        : "⏻  When done")
                {
                    DisplayStyle = icons
                        ? ToolStripItemDisplayStyle.ImageAndText
                        : ToolStripItemDisplayStyle.Text,

                    TextImageRelation =
                        TextImageRelation.ImageBeforeText,

                    ShowDropDownArrow = true,

                    Padding =
                        new Padding(4),

                    Tag = new IconTag
                    {
                        Glyph = "\uE7E8",
                        Accent = false
                    }
                };

            foreach (var a in new[]
                     {
                         PostAction.None,
                         PostAction.Shutdown,
                         PostAction.Sleep,
                         PostAction.Hibernate
                     })
            {
                var act = a;

                string label =
                    act == PostAction.None
                        ? "Do nothing"
                        : act == PostAction.Shutdown
                            ? "Shut down the computer"
                            : act == PostAction.Sleep
                                ? "Sleep"
                                : "Hibernate";

                var mi =
                    new ToolStripMenuItem(label)
                    {
                        Tag = act
                    };

                mi.Click += (s, e) =>
                {
                    afterAll = act;
                    UpdateDoneMenuText();
                };

                doneMenu.DropDownItems.Add(mi);
            }

            doneMenu.DropDownOpening += (s, e) =>
            {
                foreach (var mi in doneMenu.DropDownItems
                    .OfType<ToolStripMenuItem>())
                {
                    if (mi.Tag is PostAction pa)
                        mi.Checked =
                            pa == afterAll;
                }
            };
            UpdateDoneMenuText();
        }

        void UpdateDoneMenuText()
        {
            if (doneMenu == null) return;
            string action = afterAll == PostAction.None
                ? "Do nothing"
                : PowerActions.Name(afterAll);
            doneMenu.Text = "After downloads: " + action;
            RequestToolbarFit();
        }

        void RunAfterAllAction()
        {
            var act = afterAll;

            if (act ==
                PostAction.None)
                return;

            afterAll =
                PostAction.None;
            UpdateDoneMenuText();

            SaveState();

            using var dlg =
                new CountdownForm(
                    act,
                    30);

            Theme.Apply(dlg);

            if (dlg.ShowDialog(this) !=
                DialogResult.OK)
                return;

            SaveState();

            PowerActions.Run(act);
        }

        void ShowSettings(
            string? section = null)
        {
            using var f =
                new PreferencesForm(
                    settings,
                    section);

            Theme.Apply(f);

            if (f.ShowDialog(this) ==
                DialogResult.OK)
            {
                ApplyPreferences();
            }

            SaveState();
        }

        // Preferences সেভ হলে যা যা সঙ্গে সঙ্গে প্রয়োগ হওয়া দরকার
        void ApplyPreferences()
        {
            YtDlpCookies.Apply(settings);

            Theme.SetMode(
                settings.ThemeChoice);

            ApplyTheme();
            ApplyCompactView();

            AppLog.Enabled =
                settings.EnableLogging;

            ApplyTrafficMode();
            ApplyStartupAsync();

            RebuildList();

            if (settings.BridgeEnabled &&
                bridge == null)
            {
                StartBridge();
            }
            else if (
                !settings.BridgeEnabled &&
                bridge != null)
            {
                StopBridge();
            }
        }

        // Launch at startup: Windows-এর দিকে আসল পরিবর্তন (Store বিল্ডে StartupTask, portable-এ রেজিস্ট্রি)
        async void ApplyStartupAsync()
        {
            try
            {
                try
                {
                    bool want =
                        settings.LaunchAtStartup;

                    var st =
                        await StartupManager.SetAsync(
                            want);

                    if (want &&
                        st != StartupState.On)
                    {
                        settings.LaunchAtStartup =
                            false;

                        SaveState();

                        string why =
                            st == StartupState.DisabledByUser
                                ? "Windows has FastDM's startup turned off.\n\nOpen Task Manager → Startup apps, enable FastDM, then turn this option on again."
                                : st == StartupState.DisabledByPolicy
                                    ? "Starting FastDM with Windows is blocked by your organization's policy."
                                    : "Launch at startup is not available in this build.";

                        ThemeDialog.Show(
                            this,
                            why,
                            "Launch at startup");
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "Startup change failed: " +
                        ex.Message);
                }
            }
            catch (OperationCanceledException)
            {
                AppLog.Write("MainForm.ApplyStartupAsync: cancelled");
            }
            catch (Exception exGuard)
            {
                Program.ReportError("MainForm.ApplyStartupAsync", exGuard);
            }
        }

        // ---------- ট্রাফিক মোড (Low / Medium / High) ----------
        void BuildModeMenu()
        {
            bool icons =
                Icons.Available;

            modeMenu =
                new ToolStripDropDownButton(
                    icons
                        ? "Mode"
                        : "⚡  Mode")
                {
                    DisplayStyle = icons
                        ? ToolStripItemDisplayStyle.ImageAndText
                        : ToolStripItemDisplayStyle.Text,

                    TextImageRelation =
                        TextImageRelation.ImageBeforeText,

                    ShowDropDownArrow = true,

                    Padding =
                        new Padding(4),

                    Tag = new IconTag
                    {
                        Glyph = "\uE945",
                        Accent = false
                    }
                };

            modeMenu.DropDownOpening +=
                (s, e) => RebuildModeItems();

            RebuildModeItems();
            UpdateModeButton();
        }

        void RebuildModeItems()
        {
            modeMenu.DropDownItems.Clear();

            foreach (TrafficMode m in
                     Enum.GetValues(
                         typeof(TrafficMode)))
            {
                var mode = m;

                var mi =
                    new ToolStripMenuItem(
                        mode +
                        "   (" +
                        settings.ProfileOf(mode).Describe() +
                        ")")
                    {
                        Checked =
                            mode ==
                            settings.ActiveMode
                    };

                mi.Click += (s, e) =>
                    SetTrafficMode(mode);

                modeMenu.DropDownItems.Add(mi);
            }

            modeMenu.DropDownItems.Add(
                new ToolStripSeparator());

            modeMenu.DropDownItems.Add(
                "Edit modes…",
                null,
                (s, e) => ShowSettings(
                    "Traffic Limits"));
        }

        void UpdateModeButton()
        {
            if (modeMenu != null)
            {
                modeMenu.Text =
                    "Speed: " +
                    settings.ActiveMode;

                RequestToolbarFit();
            }
        }

        void SetTrafficMode(
            TrafficMode mode)
        {
            settings.ActiveMode =
                mode;

            ApplyTrafficMode();
            SaveState();
        }

        // সক্রিয় মোডের মান ইঞ্জিনে বসানো: গতি, মোট ও প্রতি-সার্ভারের কানেকশন, একসাথে ডাউনলোড
        void ApplyTrafficMode()
        {
            settings.TrafficMigrated = true;
            settings.ApplyActiveProfile();

            SpeedLimiter.SetKBps(
                settings.SpeedLimitKBps);

            var p =
                settings.ActiveProfile;

            Traffic.Configure(
                p.MaxConnections,
                p.PerServer);

            UpdateModeButton();
        }

        // ---------- থিম প্রয়োগ ----------
        void ApplyTheme()
        {
            var p = Theme.P;

            Theme.Apply(this);
            SuspendLayout();

            BackColor = p.Window;
            ForeColor = p.Text;

            lv.BackColor = p.Window;
            lv.ForeColor = p.Text;

            sidebar.BackColor = p.SideBg;
            dragOverlay.BackColor = p.Selection;
            if (dragOverlay.Controls.Count > 0)
            {
                dragOverlay.Controls[0].BackColor = p.Selection;
                dragOverlay.Controls[0].ForeColor = p.Text;
            }

            Theme.StyleStrip(tools);
            Theme.StyleStrip(statusBar);
            if (lv.ContextMenuStrip != null) Theme.StyleStrip(lv.ContextMenuStrip);
            Theme.StyleStrip(trayMenu);
            Theme.StyleStrip(modeMenu.DropDown);
            Theme.StyleStrip(doneMenu.DropDown);
            Theme.StyleStrip(settingsMenu.DropDown);

            lblActive.ForeColor =
                p.Accent;

            lblSpeed.ForeColor =
                p.Accent;

            lblLimit.ForeColor =
                p.Accent;

            lblSched.ForeColor =
                p.Accent;

            details?.Invalidate();

            RefreshToolIcons();
            RefreshContextMenuIcons();
            emptyIcon.Tag = null;
            RefreshEmptyStateIcon();

            Theme.SetTitleBar(this);
            Theme.StyleScroll(lv);
            Theme.StyleScroll(sidebar);

            ResumeLayout(true);

            lv.Invalidate(true);

            UpdateSideCounts();
            sidebar.Invalidate();

            Invalidate(true);
        }

        void OnSysPrefChanged(
            object? sender,
            UserPreferenceChangedEventArgs e)
        {
            if (
                e.Category !=
                    UserPreferenceCategory.General ||
                settings.ThemeChoice !=
                    ThemeMode.System)
                return;

            try
            {
                BeginInvoke(
                    new Action(() =>
                    {
                        Theme.SetMode(
                            ThemeMode.System);

                        ApplyTheme();
                    }));
            }
            catch (Exception exIgnored1) { AppLog.Write("MainForm.Settings (ignored): " + exIgnored1.Message); }
        }
    }
}
