using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FastDM
{
    // FDM-স্টাইল Preferences: বামে বিভাগের তালিকা, ডানে স্ক্রল করা সেকশন।
    // শুধু সেই সেটিংই আছে যা অ্যাপে সত্যিই কাজ করে (কোনো ফাঁকা কন্ট্রোল নেই)।
    class PreferencesForm : Form
    {
        readonly AppSettings s;

        readonly ListBox nav;
        readonly Panel content;
        readonly FlowLayoutPanel flow;

        readonly List<Control> sectionHeaders = new List<Control>();
        readonly List<Label> muted = new List<Label>();
        readonly List<Panel> lines = new List<Panel>();
        readonly List<Action> commit = new List<Action>();

        bool navBusy;

        const int RowWidth = 640;

        // ফিল্ড ভ্যালিডেশনের জন্য
        // Assigned by the Build* methods called from the constructor before events can run.
        TextBox txtFolder = null!;
        CheckBox chkAvAuto = null!, chkRunApp = null!;
        TextBox txtAvPath = null!, txtAvArgs = null!, txtAppPath = null!, txtAppArgs = null!;
        ComboBox cmbAv = null!;
        Label lblNet = null!;

        public PreferencesForm(AppSettings settings)
        {
            s = settings;

            Text = "Preferences";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(780, 520);

            // ---- নিচের বাটন বার ----
            // ডান দিক থেকে সাজানো: প্রথম যোগ করা (Cancel) সবচেয়ে ডানে, রিসাইজেও জায়গা ঠিক থাকে
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(12, 10, 12, 10)
            };

            var btnSave = new Button { Text = "Save", Size = new Size(100, 34) };
            var btnCancel = new Button { Text = "Cancel", Size = new Size(100, 34), DialogResult = DialogResult.Cancel };

            bar.Controls.Add(btnCancel);
            bar.Controls.Add(btnSave);
            btnSave.Click += OnSave;

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            // ---- বাম নেভিগেশন ----
            nav = new ListBox
            {
                Dock = DockStyle.Left,
                Width = 200,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 38,
                IntegralHeight = false
            };

            nav.DrawItem += DrawNavItem;
            nav.SelectedIndexChanged += OnNavChanged;

            // ---- ডান কনটেন্ট ----
            content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28, 0, 0, 24) };

            flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };

            content.Controls.Add(flow);

            Controls.Add(content);
            Controls.Add(nav);
            Controls.Add(bar);

            BuildGeneral();
            BuildDownloads();
            BuildBrowser();
            BuildNetwork();
            BuildAntivirus();
            BuildNotifications();
            BuildAdvanced();

            foreach (var h in sectionHeaders)
                nav.Items.Add(h.Text);

            content.Scroll += (a, b) => SyncNavToScroll();
            content.MouseWheel += (a, b) => SyncNavToScroll();
            content.MouseEnter += (a, b) => content.Focus();

            Shown += (a, b) =>
            {
                Restyle();
                nav.SelectedIndex = 0;
            };
        }

        // Theme.Apply সব লেবেলের রং এক করে দেয়, তাই হালকা লেখা আর দাগের রং এখানে আবার বসাই
        void Restyle()
        {
            foreach (var l in muted) l.ForeColor = Theme.P.SubText;
            foreach (var p in lines) p.BackColor = Theme.P.Border;

            nav.BackColor = Theme.P.SideBg;
            nav.Invalidate();
        }

        // ================= নেভিগেশন =================
        void DrawNavItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            bool sel = (e.State & DrawItemState.Selected) != 0;

            using (var bg = new SolidBrush(sel ? Theme.P.SideSel : Theme.P.SideBg))
                e.Graphics.FillRectangle(bg, e.Bounds);

            if (sel)
            {
                using var accent = new SolidBrush(Theme.P.Accent);
                e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
            }

            Font f = sel ? new Font(Font, FontStyle.Bold) : Font;

            TextRenderer.DrawText(
                e.Graphics,
                nav.Items[e.Index].ToString(),
                f,
                new Rectangle(e.Bounds.X + 18, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height),
                sel ? Theme.P.Text : Theme.P.SideText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

            if (sel)
                f.Dispose();
        }

        void OnNavChanged(object? sender, EventArgs e)
        {
            if (navBusy || nav.SelectedIndex < 0 || nav.SelectedIndex >= sectionHeaders.Count) return;

            var header = sectionHeaders[nav.SelectedIndex];
            var pt = content.PointToClient(header.PointToScreen(Point.Empty));

            content.AutoScrollPosition = new Point(0, pt.Y - content.AutoScrollPosition.Y);
        }

        void SyncNavToScroll()
        {
            int idx = 0;

            for (int i = 0; i < sectionHeaders.Count; i++)
            {
                var pt = content.PointToClient(sectionHeaders[i].PointToScreen(Point.Empty));
                if (pt.Y <= 60) idx = i;
            }

            // স্ক্রল একদম নিচে পৌঁছালে শেষ বিভাগ
            if (content.VerticalScroll.Value + content.VerticalScroll.LargeChange >= content.VerticalScroll.Maximum)
                idx = sectionHeaders.Count - 1;

            if (idx != nav.SelectedIndex)
            {
                navBusy = true;
                nav.SelectedIndex = idx;
                navBusy = false;
            }
        }

        // ================= নির্মাণ-সহায়ক =================
        Label Header(string title)
        {
            var l = new Label
            {
                Text = title,
                Tag = title,
                AutoSize = true,
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                Margin = new Padding(0, 22, 0, 4)
            };

            var line = new Panel { Height = 1, Width = RowWidth, Margin = new Padding(0, 0, 0, 10) };
            lines.Add(line);

            flow.Controls.Add(l);
            flow.Controls.Add(line);
            sectionHeaders.Add(l);
            return l;
        }

        void Sub(string text)
        {
            flow.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Margin = new Padding(0, 12, 0, 4)
            });
        }

        Label Hint(string text)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(RowWidth, 0),
                Margin = new Padding(0, 0, 0, 6)
            };

            muted.Add(l);
            flow.Controls.Add(l);
            return l;
        }

        CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                MaximumSize = new Size(RowWidth, 0),
                Margin = new Padding(0, 4, 0, 4)
            };

            commit.Add(() => set(c.Checked));
            flow.Controls.Add(c);
            return c;
        }

        NumericUpDown Num(string label, int min, int max, int value, Action<int> set)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };

            var lbl = new Label { Text = label, AutoSize = false, Width = 380, Height = 26, TextAlign = ContentAlignment.MiddleLeft };

            var n = new NumericUpDown
            {
                Width = 90,
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(value, min, max)
            };

            commit.Add(() => set((int)n.Value));
            row.Controls.Add(lbl);
            row.Controls.Add(n);
            flow.Controls.Add(row);
            return n;
        }

        // রেডিও গ্রুপ: নিজস্ব প্যানেলে, যাতে আলাদা গ্রুপ মিশে না যায়
        void Radios(string[] labels, int selected, Action<int> set)
        {
            var box = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            var list = new List<RadioButton>();

            for (int i = 0; i < labels.Length; i++)
            {
                var r = new RadioButton { Text = labels[i], AutoSize = true, Checked = i == selected, Margin = new Padding(0, 3, 0, 3) };
                list.Add(r);
                box.Controls.Add(r);
            }

            commit.Add(() => set(Math.Max(0, list.FindIndex(r => r.Checked))));
            flow.Controls.Add(box);
        }

        // পাথ বাছাইয়ের সারি: [টেক্সটবক্স][...]
        TextBox PathRow(string value, bool folder, string? filter)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
            var txt = new TextBox { Width = 500, Text = value ?? "" };
            var btn = new Button { Text = "Browse…", Width = 100, Height = 28 };

            btn.Click += (a, b) =>
            {
                if (folder)
                {
                    using var fb = new FolderBrowserDialog { SelectedPath = txt.Text };
                    if (fb.ShowDialog(this) == DialogResult.OK) txt.Text = fb.SelectedPath;
                }
                else
                {
                    using var ofd = new OpenFileDialog { Filter = filter ?? "", FileName = txt.Text };
                    if (ofd.ShowDialog(this) == DialogResult.OK) txt.Text = ofd.FileName;
                }
            };

            row.Controls.Add(txt);
            row.Controls.Add(btn);
            flow.Controls.Add(row);
            return txt;
        }

        TextBox TextRow(string label, string value)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
            row.Controls.Add(new Label { Text = label, AutoSize = false, Width = 100, Height = 26, TextAlign = ContentAlignment.MiddleLeft });

            var txt = new TextBox { Width = 400, Text = value ?? "" };
            row.Controls.Add(txt);
            flow.Controls.Add(row);
            return txt;
        }

        Button ActionButton(string text, int width, EventHandler? click)
        {
            var b = new Button { Text = text, Width = width, Height = 32, Margin = new Padding(0, 4, 0, 4) };
            if (click != null) b.Click += click;
            flow.Controls.Add(b);
            return b;
        }

        // ================= ১) General =================
        void BuildGeneral()
        {
            Header("General");

            Sub("Theme");

            var theme = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 4) };
            theme.Items.AddRange(new object[] { "Follow system", "Light", "Dark" });
            theme.SelectedIndex = Math.Clamp((int)s.ThemeChoice, 0, 2);
            commit.Add(() => s.ThemeChoice = (ThemeMode)Math.Clamp(theme.SelectedIndex, 0, 2));
            flow.Controls.Add(theme);

            Sub("Default download folder");
            txtFolder = PathRow(s.DefaultFolder, true, null);

            Check("Suggest folders based on file type (Video, Music, Documents…)", s.SuggestByType, v => s.SuggestByType = v);
            Check("Suggest folders based on download URL (site name)", s.SuggestByHost, v => s.SuggestByHost = v);
            Hint("Sub-folders are added only when you save into the default folder above. A folder you pick yourself is never changed.");

            Sub("Window");
            Check("Minimize to the system tray", s.MinimizeToTray, v => s.MinimizeToTray = v);
            Check("Close button keeps running in the tray", s.CloseToTray, v => s.CloseToTray = v);

            Sub("Update");
            Check("Check for updates on startup (portable version)", s.CheckUpdatesOnStart, v => s.CheckUpdatesOnStart = v);
        }

        // ================= ২) Downloads =================
        void BuildDownloads()
        {
            Header("Downloads");

            Check("Compact view of downloads list", s.CompactView, v => s.CompactView = v);
            Check("Automatically remove deleted files from download list", s.AutoRemoveMissing, v => s.AutoRemoveMissing = v);
            Check("Automatically remove completed downloads from download list", s.AutoRemoveCompleted, v => s.AutoRemoveCompleted = v);

            var retry = Check("Automatically retry failed downloads", s.AutoRetry, v => s.AutoRetry = v);
            var retries = Num("      Maximum retries per download", 1, 10, s.MaxRetries, v => s.MaxRetries = v);
            retries.Enabled = retry.Checked;
            retry.CheckedChanged += (a, b) => retries.Enabled = retry.Checked;

            Check("Do not download web pages", s.SkipWebPages, v => s.SkipWebPages = v);
            Check("Use server time for file creation", s.UseServerTime, v => s.UseServerTime = v);
            Check("Mark downloaded files as coming from the internet (Windows SmartScreen)", s.MarkDownloaded, v => s.MarkDownloaded = v);

            Num("Maximum urls count in batch download", 1, 1000, s.MaxBatchUrls, v => s.MaxBatchUrls = v);
        }

        // ================= ৩) Browser Integration =================
        void BuildBrowser()
        {
            Header("Browser Integration");

            Hint("Install the FastDM extension in Chrome or Edge, open its Settings and press Connect. " +
                 "Then right-click any link: FastDM → Download / Open / Create playlist.");

            Check("Allow the browser extension to connect", s.BridgeEnabled, v => s.BridgeEnabled = v);

            ActionButton("Forget paired browsers", 200, (a, b) =>
            {
                lock (s.BridgeTokens)
                    s.BridgeTokens.Clear();

                MessageBox.Show(
                    this,
                    "All paired browsers were forgotten. Each browser extension must connect (pair) again.",
                    "FastDM");
            });
        }

        // ================= ৪) Network =================
        void BuildNetwork()
        {
            Header("Network");

            lblNet = Hint(NetSummary());

            ActionButton("Proxy and speed limit…", 240, (a, b) =>
            {
                using var nf = new NetworkForm(s);
                Theme.Apply(nf);
                nf.ShowDialog(this);
                lblNet.Text = NetSummary();
            });

            ActionButton("Scheduler…", 240, (a, b) =>
            {
                using var sf = new ScheduleForm(s);
                Theme.Apply(sf);
                sf.ShowDialog(this);
            });

            Sub("Traffic");
            Num("Connections per download (1–16)", 1, 16, s.Connections, v => s.Connections = v);
            Num("Simultaneous downloads (1–10)", 1, 10, s.MaxSimultaneous, v => s.MaxSimultaneous = v);
        }

        string NetSummary()
        {
            string proxy =
                s.Proxy == ProxyMode.None ? "No proxy"
                : s.Proxy == ProxyMode.Manual ? "Manual (" + s.ProxyHost + ":" + s.ProxyPort + ")"
                : "System proxy";

            string limit = s.SpeedLimitKBps > 0 ? s.SpeedLimitKBps + " KB/s" : "Unlimited";

            return "Proxy: " + proxy + "      Speed limit: " + limit;
        }

        // ================= ৫) Antivirus =================
        void BuildAntivirus()
        {
            Header("Antivirus");

            Sub("Select antivirus");

            cmbAv = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 6) };
            cmbAv.Items.AddRange(new object[] { "Windows Defender", "Configure manually…" });
            flow.Controls.Add(cmbAv);

            Sub("Path");
            txtAvPath = PathRow(s.AntivirusPath, false, "Programs (*.exe)|*.exe|All files (*.*)|*.*");

            txtAvArgs = TextRow("Arguments:", s.AntivirusArgs);
            Hint("Arguments must contain %path% variable (it is replaced with the downloaded file).");

            chkAvAuto = Check("Automatically perform virus check when download is finished", s.AntivirusAuto, v => s.AntivirusAuto = v);

            string? defender = PostDownload.FindDefender();

            bool isDefender =
                defender != null &&
                string.Equals(s.AntivirusPath, defender, StringComparison.OrdinalIgnoreCase) &&
                s.AntivirusArgs == PostDownload.DefenderArgs;

            cmbAv.SelectedIndex = isDefender ? 0 : 1;

            cmbAv.SelectedIndexChanged += (a, b) =>
            {
                bool def = cmbAv.SelectedIndex == 0;

                if (def)
                {
                    string? d = PostDownload.FindDefender();

                    if (d == null)
                    {
                        MessageBox.Show(this, "Windows Defender was not found on this PC. Choose \"Configure manually\".", "Antivirus");
                        cmbAv.SelectedIndex = 1;
                        return;
                    }

                    txtAvPath.Text = d;
                    txtAvArgs.Text = PostDownload.DefenderArgs;
                }

                txtAvPath.Enabled = txtAvArgs.Enabled = !def;
            };

            txtAvPath.Enabled = txtAvArgs.Enabled = !isDefender;

            commit.Add(() =>
            {
                s.AntivirusPath = txtAvPath.Text.Trim();
                s.AntivirusArgs = txtAvArgs.Text.Trim();
            });
        }

        // ================= ৬) Notifications =================
        void BuildNotifications()
        {
            Header("Notifications");

            Check("Notify me of added downloads", s.NotifyAdded, v => s.NotifyAdded = v);
            Check("Notify me of completed downloads", s.NotifyCompleted, v => s.NotifyCompleted = v);
            Check("Notify me of failed downloads", s.NotifyFailed, v => s.NotifyFailed = v);
            Hint("Notifications appear only while FastDM is minimized or in the tray.");
        }

        // ================= ৭) Advanced =================
        void BuildAdvanced()
        {
            Header("Advanced");

            Sub("Automation");
            chkRunApp = Check("Launch external application on download completion", s.RunAppOnComplete, v => s.RunAppOnComplete = v);
            Hint("Path:");
            txtAppPath = PathRow(s.CompleteAppPath, false, "Programs (*.exe)|*.exe|All files (*.*)|*.*");
            txtAppArgs = TextRow("Arguments:", s.CompleteAppArgs);
            Hint("Arguments must contain %path% variable.");

            commit.Add(() =>
            {
                s.CompleteAppPath = txtAppPath.Text.Trim();
                s.CompleteAppArgs = txtAppArgs.Text.Trim();
            });

            Sub("Delete button action");
            var delOrder = new[] { DeleteAction.RemoveOnly, DeleteAction.DeleteFiles, DeleteAction.Ask };
            Radios(
                new[] { "Remove only from download list", "Delete files", "Always ask" },
                Math.Max(0, Array.IndexOf(delOrder, s.DeleteAction)),
                i => s.DeleteAction = delOrder[i]);

            Sub("File exists reaction");
            var exOrder = new[] { FileExistsAction.Rename, FileExistsAction.Overwrite, FileExistsAction.Ask };
            Radios(
                new[] { "Rename", "Overwrite", "Always ask" },
                Math.Max(0, Array.IndexOf(exOrder, s.FileExists)),
                i => s.FileExists = exOrder[i]);

            Sub("Troubleshooting");
            Check("Enable logging", s.EnableLogging, v => s.EnableLogging = v);

            ActionButton("Open log folder", 160, (a, b) =>
            {
                try
                {
                    Directory.CreateDirectory(AppLog.Folder);
                    Process.Start("explorer.exe", "\"" + AppLog.Folder + "\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "FastDM");
                }
            });

            Sub("Tools");
            var yt = ActionButton("Update yt-dlp", 160, null);

            yt.Click += async (a, b) =>
            {
                if (UpdateChecker.IsPackaged)
                {
                    MessageBox.Show(this, "In the Store version, yt-dlp is updated together with the app.", "yt-dlp");
                    return;
                }

                yt.Enabled = false;

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                    string msg = await YtDlpUpdater.UpdateAsync(cts.Token);
                    MessageBox.Show(this, msg, "yt-dlp");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "yt-dlp");
                }
                finally
                {
                    yt.Enabled = true;
                }
            };

            Sub("Go back to default settings");
            Hint("Resets everything on these pages. Paired browsers and your download list are kept.");

            ActionButton("Reset", 120, (a, b) =>
            {
                if (MessageBox.Show(
                        this,
                        "Reset all preferences to their default values?",
                        "Reset",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                s.ResetPreferences();
                DialogResult = DialogResult.OK;
                Close();
            });
        }

        // ================= সেভ =================
        bool ToolOk(bool enabled, TextBox path, TextBox args, string what, int navIndex)
        {
            if (!enabled) return true;

            string p = path.Text.Trim();

            if (p.Length == 0 || !File.Exists(p))
            {
                Warn("Please choose an existing program for " + what + ".", navIndex, path);
                return false;
            }

            if (!args.Text.Contains("%path%"))
            {
                Warn("The arguments for " + what + " must contain %path%.", navIndex, args);
                return false;
            }

            return true;
        }

        void Warn(string message, int navIndex, Control focus)
        {
            MessageBox.Show(this, message, "Preferences");
            nav.SelectedIndex = navIndex;
            focus.Focus();
        }

        void OnSave(object? sender, EventArgs e)
        {
            if (txtFolder.Text.Trim().Length == 0)
            {
                Warn("Please choose a default download folder.", 0, txtFolder);
                return;
            }

            if (!ToolOk(chkAvAuto.Checked, txtAvPath, txtAvArgs, "the antivirus", 4)) return;
            if (!ToolOk(chkRunApp.Checked, txtAppPath, txtAppArgs, "the external application", 6)) return;

            s.DefaultFolder = txtFolder.Text.Trim();

            foreach (var c in commit)
                c();

            DialogResult = DialogResult.OK;
        }
    }
}
