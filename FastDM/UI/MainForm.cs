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
    public partial class MainForm : Form
    {
        static readonly Color Green =
            ColorTranslator.FromHtml("#2ECC71");

        static readonly Color Orange =
            ColorTranslator.FromHtml("#F5A623");

        static readonly Color Red =
            ColorTranslator.FromHtml("#FF5C77");

        static readonly Color Gray =
            ColorTranslator.FromHtml("#9AA0B4");

        static readonly Font BarFont =
            new Font("Segoe UI", 8.5f, FontStyle.Bold);

        class IconTag
        {
            public string Glyph = "";
            public bool Accent;
        }

        static readonly string[] FilterNames =
        {
            "All Downloads",
            "Downloading",
            "Queued / Paused",
            "Completed",
            "Errors",
            "Deleted"
        };

        static readonly string[] FileExts =
        {
            ".zip", ".rar", ".7z", ".iso", ".exe", ".msi", ".apk", ".pdf",
            ".mp4", ".mkv", ".avi", ".mp3", ".flac", ".bin", ".dmg", ".torrent"
        };

        readonly List<DownloadItem> items =
            new List<DownloadItem>();

        AppSettings settings = new AppSettings();

        readonly string dataDir =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "FastDM");

        string DataFile =>
            Path.Combine(dataDir, "state.json");

        // These controls are created by BuildUI/BuildTray during MainForm construction before use.
        BufferedListView lv = null!;
        ListBox sidebar = null!;

        ToolStripStatusLabel lblActive = null!;
        ToolStripStatusLabel lblSpeed = null!;
        ToolStripStatusLabel lblLimit = null!;

        ToolStripDropDownButton modeMenu = null!;
        ToolStripDropDownButton doneMenu = null!;

        System.Windows.Forms.Timer navTimer = null!;

        int navTarget = 200;

        const int SidebarWidth = 200;

        readonly int[] sideCounts = new int[6];

        ToolStripStatusLabel lblSched = null!;

        DetailsPanel details = null!;

        PostAction afterAll = PostAction.None;

        bool scheduleBlocked;
        bool? lastAllowed;

        System.Windows.Forms.Timer timer = null!;

        int filter = 0;
        bool dirty;
        string? lastClip;
        DateTime lastSave = DateTime.UtcNow;

        ToolStrip tools = null!;
        StatusStrip statusBar = null!;

        NotifyIcon tray = null!;
        ContextMenuStrip trayMenu = null!;

        bool exiting;
        bool trayTipShown;

        int sessionCompleted;
        string lastCompletedName = "";
        string lastTrayTip = "";

        DateTime lastErrorNotify = DateTime.MinValue;

        EventWaitHandle? showEvent;
        BridgeServer? bridge;
        CancellationTokenSource? pipeCts;
        RegisteredWaitHandle? regWait;

        public MainForm()
        {
            InitializeComponent();

            LoadState();

            AppLog.Enabled =
                settings.EnableLogging;

            ApplyTrafficMode();

            Theme.SetMode(settings.ThemeChoice);
            NetworkApply.Load(settings);
            YtDlpCookies.Apply(settings);

            BuildUI();
            BuildTray();

            ApplyTheme();
            RebuildList();

            try
            {
                lastClip =
                    Clipboard.ContainsText()
                        ? Clipboard.GetText().Trim()
                        : null;
            }
            catch { }

            timer = new System.Windows.Forms.Timer
            {
                Interval = 500
            };

            timer.Tick += OnTick;
            timer.Start();

            Activated += (s, e) => CheckClipboard();
            Resize += OnFormResize;
            FormClosing += OnFormClosing;
            FormClosed += (s, e) => Cleanup();

            SystemEvents.UserPreferenceChanged += OnSysPrefChanged;

            SetupSingleInstanceListener();
            StartPipeListener();

            try
            {
                ProtocolHandler.RegisterForCurrentUser();
            }
            catch { }

            StartBridge();

            Shown += (s, e) => FitLastColumn();
            Shown += (s, e) => ApplyCompactView();
            Shown += (s, e) => FitToolbar();

            // Windows চালু হওয়ার সময় নিজে থেকে শুরু হলে ট্রে-তে চুপচাপ থাকে
            Shown += (s, e) =>
            {
                if (Program.StartMinimized)
                    HideToTray(false);
            };
            Shown += (s, e) => ShowStartupNotice();

            Shown += (s, e) =>
            {
                if (!string.IsNullOrEmpty(
                        Program.StartupCommand))
                {
                    HandleProtocolCommand(
                        Program.StartupCommand);
                }
            };

            Shown += async (s, e) =>
            {
                try
                {
                    await YtDlpUpdater
                        .MaybeAutoUpdateAsync(settings);

                    SaveState();
                }
                catch { }
            };

            Shown += async (s, e) =>
            {
                if (settings.CheckUpdatesOnStart)
                {
                    try
                    {
                        await UpdateChecker
                            .CheckAndShowAsync(this, true);
                    }
                    catch { }
                }
            };
        }

        // ---------- UI ----------
        void BuildUI()
        {
            Text = "Fast DM — Download Manager";
            ClientSize = new Size(1100, 640);
            MinimumSize = new Size(900, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            AllowDrop = true;

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            // ListView
            lv = new BufferedListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false,
                OwnerDraw = true,
                BorderStyle = BorderStyle.None,
                ShowItemToolTips = true,
                AllowDrop = true,
                SmallImageList = new ImageList
                {
                    ImageSize = new Size(1, 34)
                }
            };

            lv.Columns.Add("Name", 300);
            lv.Columns.Add("Size", 90);
            lv.Columns.Add("Progress", 170);
            lv.Columns.Add("Status", 100);
            lv.Columns.Add("Speed", 95);
            lv.Columns.Add("ETA", 80);
            lv.Columns.Add("Added", 120);

            lv.DrawColumnHeader += DrawHeader;
            lv.DrawItem += (s, e) => { };
            lv.DrawSubItem += DrawSub;
            lv.DoubleClick += (s, e) => OnDoubleClick();

            lv.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    RemoveSelected();
                }
                else if (e.Control && e.KeyCode == Keys.A)
                {
                    SelectAllRows();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            lv.DragEnter += OnDragEnter;
            lv.DragDrop += OnDragDrop;

            lv.ContextMenuStrip = BuildContextMenu();

            lv.SelectedIndexChanged += (s, e) =>
                details?.SetItem(
                    Sel().FirstOrDefault());

            // নিচের ডিটেইলস প্যানেল
            details = new DetailsPanel
            {
                Visible = settings.ShowDetails
            };

            details.OpenFolderRequested += OpenFolderFromDetails;
            details.OpenLinkRequested += OpenLinkFromDetails;

            // Sidebar
            sidebar = new BufferedListBox
            {
                Dock = DockStyle.Left,
                Width = 200,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 40
            };

            sidebar.Items.AddRange(FilterNames);
            sidebar.SelectedIndex = 0;

            UpdateSideCounts();

            sidebar.Width =
                settings.SidebarOpen
                    ? 200
                    : 0;

            sidebar.Visible =
                settings.SidebarOpen;

            navTarget =
                settings.SidebarOpen
                    ? 200
                    : 0;

            navTimer = new System.Windows.Forms.Timer
            {
                Interval = 15
            };

            navTimer.Tick += (s, e) => StepSidebar();

            sidebar.DrawItem += DrawSidebar;

            sidebar.SelectedIndexChanged += (s, e) =>
            {
                filter = Math.Max(
                    0,
                    sidebar.SelectedIndex);

                RebuildList();
            };

            // Status bar
            statusBar = new StatusStrip
            {
                SizingGrip = false
            };

            lblActive = new ToolStripStatusLabel("Active: 0")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblLimit =
                new ToolStripStatusLabel("Limit: Off");

            lblSched =
                new ToolStripStatusLabel("Schedule: Off");

            lblSpeed =
                new ToolStripStatusLabel("↓ 0 B/s");

            statusBar.Items.Add(lblActive);
            statusBar.Items.Add(lblSched);
            statusBar.Items.Add(lblLimit);
            statusBar.Items.Add(lblSpeed);

            // Toolbar
            tools = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(8, 6, 8, 6),
                Font = new Font("Segoe UI", 10f),
                ImageScalingSize = new Size(20, 20)
            };

            // সব toolbar action একইভাবে Icon + Text দেখাবে
            tools.Items.Add(
                Btn(
                    "Menu",
                    "☰  ",
                    "\uE700",
                    false,
                    (s, e) => ToggleSidebar(),
                    false));

            tools.Items.Add(
                new ToolStripSeparator());

            tools.Items.Add(
                Btn(
                    "Add URL",
                    "＋  ",
                    "\uE710",
                    true,
                    (s, e) => ShowAddDialog(""),
                    false));

            tools.Items.Add(
                new ToolStripSeparator());

            tools.Items.Add(
                Btn(
                    "Resume",
                    "▶  ",
                    "\uE768",
                    false,
                    (s, e) => ResumeSelected(),
                    false));

            tools.Items.Add(
                Btn(
                    "Pause",
                    "❚❚  ",
                    "\uE769",
                    false,
                    (s, e) => PauseSelected(),
                    false));

            tools.Items.Add(
                Btn(
                    "Pause All",
                    "❚❚  ",
                    "\uE769",
                    false,
                    (s, e) => PauseAll(),
                    false));

            tools.Items.Add(
                Btn(
                    "Remove",
                    "✕  ",
                    "\uE74D",
                    false,
                    (s, e) => RemoveSelected(),
                    false));

            tools.Items.Add(
                new ToolStripSeparator());

            tools.Items.Add(
                Btn(
                    "Open File",
                    "▣  ",
                    "\uE8E5",
                    false,
                    (s, e) => OpenSelected(false),
                    false));

            tools.Items.Add(
                Btn(
                    "Open Folder",
                    "▰  ",
                    "\uE838",
                    false,
                    (s, e) => OpenSelected(true),
                    false));

            tools.Items.Add(
                new ToolStripSeparator());

            BuildModeMenu();
            tools.Items.Add(modeMenu);

            tools.Items.Add(
                Btn(
                    "Schedule",
                    "⏰  ",
                    "\uE787",
                    false,
                    (s, e) => ShowSchedule(),
                    false));

            BuildDoneMenu();
            tools.Items.Add(doneMenu);

            tools.Items.Add(
                new ToolStripSeparator());

            tools.Items.Add(
                Btn(
                    "Details",
                    "▤  ",
                    "\uE946",
                    false,
                    (s, e) => ToggleDetails(),
                    false));

            tools.Items.Add(
                Btn(
                    "Settings",
                    "⚙  ",
                    "\uE713",
                    false,
                    (s, e) => ShowSettings(),
                    false));

            tools.Items.Add(
                Btn(
                    "Updates",
                    "⟳  ",
                    "\uE72C",
                    false,
                    async (s, e) =>
                        await UpdateChecker
                            .CheckAndShowAsync(this, false),
                    false));

            // ক্রম গুরুত্বপূর্ণ: Fill আগে, তারপর বাকিগুলো
            Controls.Add(lv);
            Controls.Add(details);
            Controls.Add(sidebar);
            Controls.Add(statusBar);
            Controls.Add(tools);
        }

        static ToolStripButton Btn(
            string text,
            string fallbackPrefix,
            string glyph,
            bool accent,
            EventHandler h,
            bool iconOnly = false)
        {
            bool icons = Icons.Available;

            var b = new ToolStripButton(
                icons
                    ? text
                    : fallbackPrefix + text)
            {
                ToolTipText = text,

                // সব button-এ একইভাবে icon + text
                DisplayStyle = icons
                    ? ToolStripItemDisplayStyle.ImageAndText
                    : ToolStripItemDisplayStyle.Text,

                TextImageRelation =
                    TextImageRelation.ImageBeforeText,

                Padding = new Padding(4),

                Tag = new IconTag
                {
                    Glyph = glyph,
                    Accent = accent
                }
            };

            b.Click += h;
            return b;
        }

        // ---------- toolbar: every button stays on one row ----------
        System.Windows.Forms.Timer? toolbarFitTimer;

        // Waits a moment so dragging the window edge does not re-measure on every pixel.
        void RequestToolbarFit()
        {
            if (toolbarFitTimer == null)
            {
                toolbarFitTimer =
                    new System.Windows.Forms.Timer { Interval = 60 };

                toolbarFitTimer.Tick += (s, e) =>
                {
                    toolbarFitTimer?.Stop();
                    FitToolbar();
                };
            }

            toolbarFitTimer.Stop();
            toolbarFitTimer.Start();
        }

        bool ToolbarOverflows() =>
            tools.Items
                 .Cast<ToolStripItem>()
                 .Any(i => i.Placement == ToolStripItemPlacement.Overflow);

        // Buttons that lose their text first (icon only, the name stays in the tooltip). Menu, Add URL,
        // Settings and Updates always keep their text.
        List<ToolStripItem> ToolbarCollapseOrder()
        {
            ToolStripItem? ByTip(string tip) =>
                tools.Items
                     .Cast<ToolStripItem>()
                     .FirstOrDefault(i => i.ToolTipText == tip);

            var wanted = new List<ToolStripItem?>
            {
                ByTip("Open File"), ByTip("Open Folder"), ByTip("Details"), ByTip("Schedule"),
                doneMenu, ByTip("Pause All"), ByTip("Remove"), modeMenu, ByTip("Resume"), ByTip("Pause")
            };

            return wanted.Where(i => i != null).Select(i => i!).ToList();
        }

        // 1) normal spacing  2) tighter spacing  3) icon only for the least used buttons, one by one,
        // until nothing is pushed into the "»" overflow menu.
        void FitToolbar()
        {
            if (tools == null || !tools.IsHandleCreated)
                return;

            var all = tools.Items.Cast<ToolStripItem>().ToList();
            var order = ToolbarCollapseOrder();

            tools.SuspendLayout();

            foreach (var i in all)
            {
                if (i is ToolStripSeparator)
                    continue;

                i.Padding = new Padding(4);
            }

            foreach (var i in order)
            {
                i.DisplayStyle =
                    Icons.Available
                        ? ToolStripItemDisplayStyle.ImageAndText
                        : ToolStripItemDisplayStyle.Text;
            }

            tools.ResumeLayout(true);
            tools.PerformLayout();

            if (ToolbarOverflows())
            {
                foreach (var i in all)
                {
                    if (i is ToolStripSeparator)
                        continue;

                    i.Padding = new Padding(2, 4, 2, 4);
                }

                tools.PerformLayout();
            }

            if (!Icons.Available)
                return;

            foreach (var i in order)
            {
                if (!ToolbarOverflows())
                    break;

                if (string.IsNullOrEmpty(i.ToolTipText) || i is ToolStripDropDownButton)
                    i.ToolTipText = i.Text;

                i.DisplayStyle = ToolStripItemDisplayStyle.Image;
                tools.PerformLayout();
            }
        }

        void RefreshToolIcons()
        {
            if (!Icons.Available)
                return;

            foreach (ToolStripItem item in tools.Items)
            {
                if (item.Tag is IconTag tag)
                {
                    var old = item.Image;

                    item.Image = Icons.Make(
                        tag.Glyph,
                        tag.Accent
                            ? Theme.P.Accent
                            : Theme.P.Text,
                        20);

                    old?.Dispose();
                }
            }
        }

        ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip();

            m.Items.Add(
                "Resume",
                null,
                (s, e) => ResumeSelected());

            m.Items.Add(
                "Pause",
                null,
                (s, e) => PauseSelected());

            m.Items.Add(
                "Start now (ignore schedule)",
                null,
                (s, e) => StartNowSelected());

            m.Items.Add(
                new ToolStripSeparator());

            m.Items.Add(
                "Open File",
                null,
                (s, e) => OpenSelected(false));

            m.Items.Add(
                "Open Folder",
                null,
                (s, e) => OpenSelected(true));

            m.Items.Add(
                "Copy URL",
                null,
                (s, e) =>
                {
                    var sel = Sel();

                    if (sel.Count > 0)
                    {
                        Clipboard.SetText(
                            string.Join(
                                Environment.NewLine,
                                sel.Select(i => i.Url)));
                    }
                });

            m.Items.Add(
                new ToolStripSeparator());

            m.Items.Add(
                "Remove",
                null,
                (s, e) => RemoveSelected());

            // only in the Deleted list
            m.Items.Add("Download again", null, (s, e) => DownloadAgain());
            m.Items.Add("Remove from history", null, (s, e) => RemoveSelected());
            m.Items.Add("Clear history", null, (s, e) => ClearHistory());

            m.Opening += (s, e) =>
            {
                bool inDeleted = filter == DeletedFilter;

                foreach (ToolStripItem i in m.Items)
                {
                    string text = i.Text ?? "";

                    bool deletedOnly =
                        text == "Download again" ||
                        text == "Remove from history" ||
                        text == "Clear history";

                    bool liveOnly =
                        text == "Resume" ||
                        text == "Pause" ||
                        text.StartsWith("Start now") ||
                        text == "Open File" ||
                        text == "Remove";

                    if (deletedOnly)
                        i.Visible = inDeleted;
                    else if (liveOnly)
                        i.Visible = !inDeleted;
                }
            };

            return m;
        }

        // ---------- ড্রয়িং ----------
        void DrawSidebar(
            object? sender,
            DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            var p = Theme.P;
            var g = e.Graphics;

            bool sel =
                (e.State & DrawItemState.Selected) != 0;

            using (var b = new SolidBrush(
                sel ? p.SideSel : p.SideBg))
            {
                g.FillRectangle(b, e.Bounds);
            }

            if (sel)
            {
                using (var b = new SolidBrush(p.Accent))
                {
                    g.FillRectangle(
                        b,
                        e.Bounds.X,
                        e.Bounds.Y,
                        4,
                        e.Bounds.Height);
                }
            }

            var tr = new Rectangle(
                e.Bounds.X + 18,
                e.Bounds.Y,
                SidebarWidth - 70,
                e.Bounds.Height);

            TextRenderer.DrawText(
                g,
                FilterNames[e.Index],
                Font,
                tr,
                p.SideText,
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.Left);

            int count =
                e.Index < sideCounts.Length
                    ? sideCounts[e.Index]
                    : 0;

            var cr = new Rectangle(
                e.Bounds.X + SidebarWidth - 55,
                e.Bounds.Y,
                45,
                e.Bounds.Height);

            TextRenderer.DrawText(
                g,
                count.ToString(),
                Font,
                cr,
                p.SideCount,
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.Right);
        }

        void DrawHeader(
            object? sender,
            DrawListViewColumnHeaderEventArgs e)
        {
            var p = Theme.P;

            using (var b = new SolidBrush(p.Header))
                e.Graphics.FillRectangle(
                    b,
                    e.Bounds);

            using (var pen = new Pen(p.Border))
            {
                e.Graphics.DrawLine(
                    pen,
                    e.Bounds.Left,
                    e.Bounds.Bottom - 1,
                    e.Bounds.Right,
                    e.Bounds.Bottom - 1);

                e.Graphics.DrawLine(
                    pen,
                    e.Bounds.Right - 1,
                    e.Bounds.Top + 6,
                    e.Bounds.Right - 1,
                    e.Bounds.Bottom - 6);
            }

            var tr = new Rectangle(
                e.Bounds.X + 8,
                e.Bounds.Y,
                Math.Max(
                    0,
                    e.Bounds.Width - 12),
                e.Bounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                e.Header?.Text ?? "",
                Font,
                tr,
                p.SubText,
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.Left |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        void DrawSub(
            object? sender,
            DrawListViewSubItemEventArgs e)
        {
            var p = Theme.P;
            var it = (DownloadItem)e.Item.Tag;
            var g = e.Graphics;

            var r = new Rectangle(
                e.Bounds.X,
                e.Bounds.Y,
                lv.Columns[e.ColumnIndex].Width,
                e.Bounds.Height);

            Color back =
                e.Item.Selected
                    ? p.Selection
                    : (e.ItemIndex % 2 == 0
                        ? p.Window
                        : p.RowAlt);

            using (var b = new SolidBrush(back))
                g.FillRectangle(b, r);

            var flags =
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.Left |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix;

            if (e.ColumnIndex == 0)
            {
                g.SmoothingMode =
                    SmoothingMode.AntiAlias;

                using (var b = new SolidBrush(
                    RowColor(it)))
                {
                    g.FillEllipse(
                        b,
                        r.X + 8,
                        r.Y + (r.Height - 10) / 2,
                        10,
                        10);
                }

                var tr = new Rectangle(
                    r.X + 26,
                    r.Y,
                    r.Width - 30,
                    r.Height);

                TextRenderer.DrawText(
                    g,
                    e.SubItem?.Text ?? "",
                    Font,
                    tr,
                    p.Text,
                    flags);
            }
            else if (e.ColumnIndex == 2)
            {
                var bar = new Rectangle(
                    r.X + 6,
                    r.Y + (r.Height - 18) / 2,
                    r.Width - 14,
                    18);

                g.SmoothingMode =
                    SmoothingMode.AntiAlias;

                using (var gp = RoundRect(bar, 8))
                using (var b = new SolidBrush(p.Track))
                {
                    g.FillPath(b, gp);
                }

                string label;

                if (it.TotalBytes > 0)
                {
                    int w = (int)(
                        bar.Width *
                        it.Percent /
                        100.0);

                    if (w > 6)
                    {
                        var fill = new Rectangle(
                            bar.X,
                            bar.Y,
                            w,
                            bar.Height);

                        using (var gp =
                               RoundRect(fill, 8))
                        using (var b =
                               new SolidBrush(
                                   RowColor(it)))
                        {
                            g.FillPath(b, gp);
                        }
                    }

                    label =
                        it.Percent.ToString("0.0") +
                        "%";
                }
                else
                {
                    label =
                        it.Downloaded > 0
                            ? Fmt(it.Downloaded)
                            : "—";
                }

                TextRenderer.DrawText(
                    g,
                    label,
                    BarFont,
                    bar,
                    p.Text,
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.HorizontalCenter);
            }
            else
            {
                var tr = new Rectangle(
                    r.X + 6,
                    r.Y,
                    r.Width - 8,
                    r.Height);

                TextRenderer.DrawText(
                    g,
                    e.SubItem?.Text ?? "",
                    Font,
                    tr,
                    p.Text,
                    flags);
            }
        }

        static GraphicsPath RoundRect(
            Rectangle r,
            int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();

            p.AddArc(
                r.X,
                r.Y,
                d,
                d,
                180,
                90);

            p.AddArc(
                r.Right - d,
                r.Y,
                d,
                d,
                270,
                90);

            p.AddArc(
                r.Right - d,
                r.Bottom - d,
                d,
                d,
                0,
                90);

            p.AddArc(
                r.X,
                r.Bottom - d,
                d,
                d,
                90,
                90);

            p.CloseFigure();

            return p;
        }

        static Color RowColor(DownloadItem it) =>
            it.DeletedAt.HasValue ? Gray : StateColor(it.State);

        static Color StateColor(DlState s)
        {
            switch (s)
            {
                case DlState.Downloading:
                    return Theme.P.Accent;

                case DlState.Completed:
                    return Green;

                case DlState.Paused:
                    return Orange;

                case DlState.Error:
                    return Red;

                default:
                    return Gray;
            }
        }

        // ---------- লিস্ট ----------
        static bool Match(
            int f,
            DownloadItem i)
        {
            switch (f)
            {
                case 1:
                    return i.State == DlState.Downloading;

                case 2:
                    return i.State == DlState.Queued ||
                           i.State == DlState.Paused;

                case 3:
                    return i.State == DlState.Completed;

                case 4:
                    return i.State == DlState.Error;

                default:
                    return true;
            }
        }

        // Id of the download that was just added: the next list rebuild selects it, so the details panel shows it.
        string? selectNewId;

        // What the user removed, newest first. Shown under "Deleted" in the sidebar, kept in state.json.
        readonly List<DownloadItem> deleted = new List<DownloadItem>();
        const int DeletedFilter = 5;
        const int MaxDeletedHistory = 500;

        IEnumerable<DownloadItem> VisibleItems() =>
            filter == DeletedFilter
                ? deleted.OrderByDescending(i => i.DeletedAt ?? i.Added)
                : items.Where(i => Match(filter, i)).OrderByDescending(i => i.Added);

        // Keeps a copy of a download that is leaving the list, with the time and the reason.
        void RecordDeleted(DownloadItem it, string reason)
        {
            var rec = new DownloadItem
            {
                Url = it.Url,
                FileName = it.FileName,
                Folder = it.Folder,
                Referer = it.Referer,
                UserAgent = it.UserAgent,
                ContentType = it.ContentType,
                TotalBytes = it.TotalBytes,
                SupportsRange = it.SupportsRange,
                Stream = it.Stream,
                YtDlp = it.YtDlp,
                Added = it.Added,
                State = it.State == DlState.Downloading ? DlState.Paused : it.State,
                Error = it.Error,
                DeletedAt = DateTime.Now,
                DeletedReason = reason
            };

            rec.Downloaded = it.Downloaded;

            deleted.Insert(0, rec);

            while (deleted.Count > MaxDeletedHistory)
                deleted.RemoveAt(deleted.Count - 1);
        }

        // "Download again" in the Deleted list: a fresh download from the same link, folder and options.
        void DownloadAgain()
        {
            var sel = Sel().Where(i => i.DeletedAt.HasValue && !string.IsNullOrEmpty(i.Url)).ToList();

            if (sel.Count == 0)
                return;

            if (MessageBox.Show(
                    this,
                    "Download " + sel.Count + " item(s) again?",
                    "Deleted",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
                return;

            foreach (var d in sel)
            {
                var it = new DownloadItem
                {
                    Url = d.Url,
                    Folder = d.Folder,
                    FileName = d.FileName,
                    Referer = d.Referer,
                    UserAgent = d.UserAgent,
                    ContentType = d.ContentType,
                    TotalBytes = d.TotalBytes,
                    SupportsRange = d.SupportsRange,
                    Stream = d.Stream,
                    YtDlp = d.YtDlp,
                    NeedsProbe = Engine.IsHttp(d.Url) && d.TotalBytes <= 0,
                    State = DlState.Queued
                };

                deleted.Remove(d);
                items.Add(it);
                selectNewId = it.Id;
            }

            MarkChanged();
            SaveState();

            lblActive.Text = sel.Count + " download(s) added again.";
        }

        void RemoveFromHistory(List<DownloadItem> sel)
        {
            if (MessageBox.Show(
                    this,
                    "Remove " + sel.Count + (sel.Count == 1 ? " entry" : " entries") +
                    " from the Deleted list?\n\nFiles on your disk are not touched.",
                    "Deleted",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
                return;

            foreach (var d in sel)
                deleted.Remove(d);

            MarkChanged();
            SaveState();
        }

        void ClearHistory()
        {
            if (deleted.Count == 0)
                return;

            if (MessageBox.Show(
                    this,
                    "Clear the whole Deleted list (" + deleted.Count + " entries)?\n\nFiles on your disk are not touched.",
                    "Deleted",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
                return;

            deleted.Clear();

            MarkChanged();
            SaveState();
        }

        void RebuildList()
        {
            var selected =
                lv.SelectedItems
                  .Cast<ListViewItem>()
                  .Select(l => l.Tag)
                  .OfType<DownloadItem>()
                  .Select(it => it.Id)
                  .ToHashSet();

            lv.BeginUpdate();

            if (lv.Columns.Count > 6)
                lv.Columns[6].Text = filter == DeletedFilter ? "Deleted" : "Added";

            lv.Items.Clear();

            foreach (var it in VisibleItems())
            {
                var lvi =
                    new ListViewItem(
                        new[]
                        {
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            ""
                        })
                    {
                        Tag = it
                    };

                lv.Items.Add(lvi);

                FillRow(lvi, it);

                if (selected.Contains(it.Id))
                    lvi.Selected = true;
            }

            lv.EndUpdate();

            if (selectNewId != null)
            {
                string id = selectNewId;
                selectNewId = null;

                var target =
                    lv.Items
                      .Cast<ListViewItem>()
                      .FirstOrDefault(l => l.Tag is DownloadItem d && d.Id == id);

                if (target != null)
                {
                    foreach (ListViewItem l in lv.SelectedItems.Cast<ListViewItem>().ToList())
                        l.Selected = false;

                    target.Selected = true;
                    target.Focused = true;
                    target.EnsureVisible();
                }
            }

            UpdateSideCounts();
            sidebar.Invalidate();
        }

        static void SetIf(
            ListViewItem l,
            int idx,
            string text)
        {
            if (l.SubItems[idx].Text != text)
                l.SubItems[idx].Text = text;
        }

        static void FillDeletedRow(
            ListViewItem l,
            DownloadItem it)
        {
            SetIf(l, 0, it.FileName ?? "");

            SetIf(
                l,
                1,
                it.TotalBytes > 0
                    ? Fmt(it.TotalBytes)
                    : it.Downloaded > 0
                        ? Fmt(it.Downloaded)
                        : "\u2014");

            SetIf(l, 2, it.Percent.ToString("0.0") + "%");
            SetIf(l, 3, it.DeletedReason ?? "Deleted");
            SetIf(l, 4, "");
            SetIf(l, 5, "");
            SetIf(l, 6, (it.DeletedAt ?? it.Added).ToString("dd MMM HH:mm"));

            l.ToolTipText = it.Url;
        }

        void FillRow(
            ListViewItem l,
            DownloadItem it)
        {
            if (it.DeletedAt.HasValue)
            {
                FillDeletedRow(l, it);
                return;
            }

            SetIf(
                l,
                0,
                it.FileName);

            SetIf(
                l,
                1,
                it.TotalBytes > 0
                    ? (
                        it.Stream != null &&
                        it.State != DlState.Completed
                            ? "~"
                            : "") +
                      Fmt(it.TotalBytes)
                    : "—");

            SetIf(
                l,
                2,
                it.Percent.ToString("0.0") + "%");

            SetIf(
                l,
                3,
                it.State == DlState.Downloading &&
                !string.IsNullOrEmpty(it.StatusNote)
                    ? it.StatusNote
                    : it.State == DlState.Queued &&
                      scheduleBlocked &&
                      !it.IgnoreSchedule
                        ? "Scheduled"
                        : it.State == DlState.Paused &&
                          it.PausedBySchedule
                            ? "Paused (schedule)"
                            : StateText(it.State));

            SetIf(
                l,
                4,
                it.State == DlState.Downloading
                    ? Fmt(it.Speed) + "/s"
                    : "");

            SetIf(
                l,
                5,
                Eta(it));

            SetIf(
                l,
                6,
                it.Added.ToString("dd MMM HH:mm"));

            l.ToolTipText =
                it.State == DlState.Error
                    ? it.Error
                    : it.Url;
        }

        static string StateText(DlState s) =>
            s == DlState.Completed
                ? "Complete"
                : s.ToString();

        static string Fmt(double b)
        {
            string[] u =
            {
                "B",
                "KB",
                "MB",
                "GB",
                "TB"
            };

            int i = 0;

            while (b >= 1024 &&
                   i < u.Length - 1)
            {
                b /= 1024;
                i++;
            }

            return (
                i == 0
                    ? b.ToString("0")
                    : b.ToString("0.##"))
                   + " " +
                   u[i];
        }

        static string Eta(DownloadItem it)
        {
            if (it.State != DlState.Downloading ||
                it.Speed < 1 ||
                it.TotalBytes <= 0)
                return "";

            var t = TimeSpan.FromSeconds(
                (it.TotalBytes - it.Downloaded) /
                it.Speed);

            if (t.TotalHours >= 1)
                return (int)t.TotalHours +
                       "h " +
                       t.Minutes +
                       "m";

            if (t.TotalMinutes >= 1)
                return t.Minutes +
                       "m " +
                       t.Seconds +
                       "s";

            return t.Seconds + "s";
        }

        void SelectAllRows()
        {
            lv.BeginUpdate();

            try
            {
                foreach (ListViewItem l in lv.Items)
                    l.Selected = true;
            }
            finally
            {
                lv.EndUpdate();
            }
        }

        List<DownloadItem> Sel() =>
            lv.SelectedItems
              .Cast<ListViewItem>()
              .Select(l => l.Tag)
              .OfType<DownloadItem>()
              .ToList();

        void MarkChanged()
        {
            dirty = true;
        }

        // ---------- টাইমার: কিউ + স্পিড ----------
        void OnTick(
            object? sender,
            EventArgs e)
        {
            bool schedOn =
                settings.SchedulerEnabled;

            bool allowed =
                !schedOn ||
                Scheduler.IsAllowed(
                    settings,
                    DateTime.Now);

            scheduleBlocked =
                schedOn &&
                !allowed;

            if (lastAllowed != allowed)
            {
                lastAllowed = allowed;

                if (!allowed)
                {
                    if (settings.ScheduleStopOutside)
                    {
                        foreach (var it in items
                            .Where(i =>
                                i.State ==
                                    DlState.Downloading &&
                                !i.IgnoreSchedule)
                            .ToList())
                        {
                            it.PausedBySchedule = true;
                            it.Cts?.Cancel();
                        }
                    }
                }
                else
                {
                    foreach (var it in items
                        .Where(i =>
                            i.PausedBySchedule &&
                            i.State == DlState.Paused)
                        .ToList())
                    {
                        it.PausedBySchedule = false;
                        it.State = DlState.Queued;
                    }
                }

                MarkChanged();
            }

            int active =
                items.Count(
                    i => i.State ==
                         DlState.Downloading);

            foreach (var it in items
                .Where(i => i.State == DlState.Queued)
                .OrderBy(i => i.Added)
                .ToList())
            {
                if (active >=
                    settings.MaxSimultaneous)
                    break;

                if (scheduleBlocked &&
                    !it.IgnoreSchedule)
                    continue;

                StartDownload(it);
                active++;
            }

            var now = DateTime.UtcNow;
            double total = 0;

            foreach (var it in items)
            {
                if (it.State ==
                    DlState.Downloading)
                {
                    double dt =
                        (now - it.LastTick)
                            .TotalSeconds;

                    if (dt >= 0.4)
                    {
                        long cur =
                            it.Downloaded;

                        double inst =
                            (cur - it.LastBytes) /
                            dt;

                        it.Speed =
                            it.Speed <= 0
                                ? inst
                                : it.Speed * 0.6 +
                                  inst * 0.4;

                        it.LastBytes = cur;
                        it.LastTick = now;
                    }

                    total += it.Speed;

                    it.SpeedHistory.Add(
                        it.Speed);

                    if (it.SpeedHistory.Count >
                        DetailsPanel.HistoryLength)
                    {
                        it.SpeedHistory.RemoveAt(0);
                    }
                }
                else
                {
                    it.Speed = 0;
                }
            }

            if (dirty)
            {
                dirty = false;
                RebuildList();
            }
            else
            {
                foreach (ListViewItem l in lv.Items)
                {
                    if (l.Tag is DownloadItem item)
                        FillRow(l, item);
                }

                lv.Invalidate();
            }

            int actNow =
                items.Count(
                    i => i.State ==
                         DlState.Downloading);

            int queNow =
                items.Count(
                    i => i.State ==
                         DlState.Queued);

            lblActive.Text =
                "Active: " +
                actNow +
                "   Queued: " +
                queNow;

            lblSpeed.Text =
                "↓ " +
                Fmt(total) +
                "/s";

            string limText =
                "Mode: " +
                settings.ActiveMode +
                (settings.SpeedLimitKBps > 0
                    ? " · " +
                      SpeedLimiter.Describe(
                          settings.SpeedLimitKBps)
                    : "");

            if (lblLimit.Text != limText)
                lblLimit.Text = limText;

            string schedText =
                Scheduler.Status(
                    settings,
                    DateTime.Now,
                    allowed);

            if (afterAll !=
                PostAction.None)
            {
                schedText +=
                    "   When done: " +
                    PowerActions.Name(afterAll);
            }

            if (lblSched.Text != schedText)
                lblSched.Text = schedText;

            if (details.Visible &&
                details.Item != null)
            {
                details.Invalidate();
            }

            PeriodicMaintenance();
            PauseSlowDownloads();

            UpdateSideCounts();
            sidebar.Invalidate();

            string tip =
                actNow > 0
                    ? "FastDM — ↓ " +
                      Fmt(total) +
                      "/s"
                    : "FastDM";

            if (tray != null &&
                tip != lastTrayTip)
            {
                tray.Text = tip;
                lastTrayTip = tip;
            }

            if (sessionCompleted > 0 &&
                actNow == 0 &&
                queNow == 0)
            {
                if (settings.NotifyCompleted)
                    Notify(
                        "Download complete",
                        sessionCompleted == 1
                            ? lastCompletedName
                            : sessionCompleted +
                              " downloads completed");

                sessionCompleted = 0;

                RunAfterAllAction();
            }

            if (active > 0 &&
                (now - lastSave).TotalSeconds > 3)
            {
                SaveState();
            }
        }

        // ---------- ডাউনলোড কন্ট্রোল ----------
        async void StartDownload(
            DownloadItem it)
        {
            var cts =
                new CancellationTokenSource();

            it.Cts = cts;
            it.State = DlState.Downloading;
            it.Error = null;
            it.PausedBySchedule = false;

            // ম্যানুয়াল শুরু/রিজিউমে হলে অটো-রিট্রাইয়ের গণনা নতুন করে
            if (!it.AutoRetrying)
                it.RetryCount = 0;

            it.AutoRetrying = false;
            it.SlowSince = null;

            AppLog.Write(
                "Start: " +
                it.FileName);

            it.SpeedHistory.Clear();
            it.LastBytes = it.Downloaded;
            it.LastTick = DateTime.UtcNow;
            it.Speed = 0;

            MarkChanged();

            try
            {
                if (it.YtDlp != null)
                {
                    await YtDlpEngine.RunAsync(
                        it,
                        settings.Connections,
                        cts.Token);
                }
                else if (it.Stream != null)
                {
                    await StreamEngine.RunAsync(
                        it,
                        settings.Connections,
                        cts.Token);
                }
                else
                {
                    await Engine.RunItemAsync(
                        it,
                        settings.Connections,
                        cts.Token);
                }

                if (it.TotalBytes <= 0)
                    it.TotalBytes =
                        it.Downloaded;

                it.State =
                    DlState.Completed;
            }
            catch (OperationCanceledException)
            {
                it.State =
                    DlState.Paused;
            }
            catch (Exception ex)
            {
                it.State =
                    DlState.Error;

                it.Error =
                    ex.Message;

                AppLog.Write(
                    "Error: " +
                    it.FileName +
                    " — " +
                    ex.Message);
            }
            finally
            {
                it.Cts = null;
                it.StatusNote = "";
                cts.Dispose();
            }

            // ধীর বলে সরানো ডাউনলোড: লাইনের শেষে গিয়ে অপেক্ষা করবে, অন্যগুলো আগে চলবে
            if (it.RequeueAfterPause)
            {
                it.RequeueAfterPause = false;

                if (it.State ==
                        DlState.Paused &&
                    !it.RemoveRequested)
                {
                    it.Added =
                        DateTime.Now;

                    it.State =
                        DlState.Queued;
                }
            }

            if (!it.RemoveRequested)
            {
                if (it.State ==
                    DlState.Completed)
                {
                    sessionCompleted++;
                    lastCompletedName =
                        it.FileName;

                    FinishDownload(it);
                }
                else if (it.State ==
                         DlState.Error)
                {
                    if (settings.AutoRetry &&
                        it.RetryCount <
                            Math.Max(1, settings.MaxRetries))
                    {
                        it.RetryCount++;
                        ScheduleRetry(it);
                    }
                    else
                    {
                        NotifyError(it);
                    }
                }
            }

            it.Speed = 0;

            if (it.RemoveRequested)
            {
                TryDelete(it.TempPath);
                TryDelete(it.PartsDir);
                YtDlpEngine.Cleanup(it);

                if (it.State ==
                        DlState.Completed &&
                    it.DeleteFile)
                {
                    TryDelete(it.SavePath);
                }
            }

            MarkChanged();
            SaveState();
        }

        // ডাউনলোড শেষের কাজ: সার্ভারের সময়, Mark of the Web, অ্যান্টিভাইরাস, বাইরের অ্যাপ, লিস্ট থেকে সরানো
        void FinishDownload(
            DownloadItem it)
        {
            it.RetryCount = 0;

            AppLog.Write(
                "Completed: " +
                it.SavePath);

            var s = settings;

            // ডিস্ক আর বাইরের প্রোগ্রাম UI আটকাবে না
            Task.Run(
                () => PostDownload.Apply(
                    s,
                    it));

            if (s.AutoRemoveCompleted)
                items.Remove(it);
        }

        // ব্যর্থ ডাউনলোড কিছুক্ষণ পর নিজে আবার চেষ্টা (৫, ১০, ১৫… সেকেন্ড, সর্বোচ্চ ৬০)
        async void ScheduleRetry(
            DownloadItem it)
        {
            AppLog.Write(
                "Retry " +
                it.RetryCount +
                ": " +
                it.FileName);

            await Task.Delay(
                TimeSpan.FromSeconds(
                    Math.Min(
                        60,
                        5 * it.RetryCount)));

            if (items.Contains(it) &&
                it.State ==
                    DlState.Error &&
                !it.RemoveRequested)
            {
                it.AutoRetrying = true;
                it.State =
                    DlState.Queued;
                it.Error = null;

                MarkChanged();
            }
        }

        // "Pause slow downloads and let others go first": অন্য ডাউনলোড লাইনে অপেক্ষা করলে
        // এবং এটা নির্ধারিত সময় ধরে সীমার চেয়ে ধীর হলে সরিয়ে লাইনের শেষে পাঠায়
        void PauseSlowDownloads()
        {
            if (!settings.PauseSlow)
                return;

            var now =
                DateTime.UtcNow;

            double limit =
                settings.PauseSlowKBps *
                1024.0;

            int waiting =
                items.Count(
                    i => i.State ==
                             DlState.Queued &&
                         (!scheduleBlocked ||
                          i.IgnoreSchedule));

            foreach (var it in items
                .Where(i =>
                    i.State ==
                    DlState.Downloading)
                .ToList())
            {
                if (it.Speed > limit)
                {
                    it.SlowSince = null;
                    continue;
                }

                if (it.SlowSince == null)
                {
                    it.SlowSince = now;
                    continue;
                }

                if (waiting > 0 &&
                    it.Cts != null &&
                    (now - it.SlowSince.Value)
                        .TotalMinutes >=
                    Math.Max(1, settings.PauseSlowMinutes))
                {
                    AppLog.Write(
                        "Slow download moved back: " +
                        it.FileName);

                    it.SlowSince = null;
                    it.RequeueAfterPause = true;
                    it.Cts?.Cancel();

                    waiting--;
                }
            }
        }

        // ডিস্ক থেকে মুছে ফেলা ফাইল লিস্ট থেকেও সরানো (প্রতি ৩০ সেকেন্ডে একবার)
        DateTime lastMissingCheck =
            DateTime.MinValue;

        void PeriodicMaintenance()
        {
            if (!settings.AutoRemoveMissing)
                return;

            if ((DateTime.UtcNow -
                 lastMissingCheck)
                    .TotalSeconds < 30)
                return;

            lastMissingCheck =
                DateTime.UtcNow;

            var gone =
                items
                    .Where(i =>
                        i.State ==
                            DlState.Completed &&
                        FileCheck.IsDeleted(
                            i.Folder,
                            i.FileName))
                    .ToList();

            if (gone.Count == 0)
                return;

            foreach (var g in gone)
            {
                RecordDeleted(g, "File deleted from disk");
                items.Remove(g);
            }

            AppLog.Write(
                "Moved " +
                gone.Count +
                " missing file(s) to the Deleted list");

            MarkChanged();
            SaveState();
        }

        void ResumeSelected()
        {
            if (filter == DeletedFilter)
                return;

            foreach (var it in Sel())
            {
                if (it.State ==
                        DlState.Paused ||
                    it.State ==
                        DlState.Error)
                {
                    it.State = DlState.Queued;
                    it.Error = null;
                }
            }

            MarkChanged();
        }

        void PauseSelected()
        {
            if (filter == DeletedFilter)
                return;

            foreach (var it in Sel())
            {
                if (it.State ==
                    DlState.Downloading)
                {
                    it.Cts?.Cancel();
                }
                else if (it.State ==
                         DlState.Queued)
                {
                    it.State =
                        DlState.Paused;
                }
            }

            MarkChanged();
        }

        void PauseAll()
        {
            foreach (var it in items)
            {
                if (it.State ==
                    DlState.Downloading)
                {
                    it.Cts?.Cancel();
                }
                else if (it.State ==
                         DlState.Queued)
                {
                    it.State =
                        DlState.Paused;
                }
            }

            MarkChanged();
        }

        void ResumeAll()
        {
            foreach (var it in items)
            {
                if (it.State ==
                        DlState.Paused ||
                    it.State ==
                        DlState.Error)
                {
                    it.State =
                        DlState.Queued;

                    it.Error = null;
                }
            }

            MarkChanged();
        }

        void RemoveSelected()
        {
            var sel = Sel();

            if (sel.Count == 0)
                return;

            // in the Deleted list, Remove forgets the history entries
            if (filter == DeletedFilter)
            {
                RemoveFromHistory(sel);
                return;
            }

            bool deleteDone = false;

            // Preferences → Advanced → Delete button action
            if (settings.DeleteAction !=
                DeleteAction.Ask)
            {
                deleteDone =
                    settings.DeleteAction ==
                        DeleteAction.DeleteFiles &&
                    sel.Any(
                        i => i.State ==
                             DlState.Completed);
            }
            else if (sel.Any(
                    i => i.State ==
                         DlState.Completed))
            {
                var r = MessageBox.Show(
                    this,
                    "Remove " +
                    sel.Count +
                    " download(s) from the list?\n\n" +
                    "Yes = remove from list only\n" +
                    "No = remove AND delete completed file(s) from disk",
                    "Remove",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (r == DialogResult.Cancel)
                    return;

                deleteDone =
                    r == DialogResult.No;
            }
            else
            {
                if (MessageBox.Show(
                        this,
                        "Remove " +
                        sel.Count +
                        " download(s)? Partial data will be deleted.",
                        "Remove",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question)
                    != DialogResult.OK)
                {
                    return;
                }
            }

            foreach (var it in sel)
            {
                it.RemoveRequested = true;
                it.DeleteFile = deleteDone;

                RecordDeleted(
                    it,
                    it.State == DlState.Completed
                        ? (deleteDone ? "Removed, file deleted from disk" : "Removed from the list")
                        : "Removed before it finished");

                items.Remove(it);

                if (it.State ==
                    DlState.Downloading)
                {
                    it.Cts?.Cancel();
                }
                else
                {
                    TryDelete(it.TempPath);
                    TryDelete(it.PartsDir);

                    YtDlpEngine.Cleanup(it);

                    if (it.State ==
                            DlState.Completed &&
                        deleteDone)
                    {
                        TryDelete(it.SavePath);
                    }
                }
            }

            MarkChanged();
            SaveState();
        }

        static void TryDelete(
            string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                else if (Directory.Exists(path))
                    Directory.Delete(
                        path,
                        true);
            }
            catch { }
        }

        void OpenSelected(bool folder)
        {
            var sel = Sel();

            if (sel.Count == 0)
                return;

            var it = sel[0];

            try
            {
                if (folder)
                {
                    RevealInExplorer(it);
                }
                else
                {
                    if (it.DeletedAt.HasValue &&
                        !File.Exists(it.SavePath))
                    {
                        MessageBox.Show(
                            this,
                            "This file was deleted from the disk.");

                        return;
                    }

                    if (it.State !=
                            DlState.Completed ||
                        !File.Exists(
                            it.SavePath))
                    {
                        MessageBox.Show(
                            this,
                            "File is not completed yet.");

                        return;
                    }

                    Process.Start(
                        new ProcessStartInfo(
                            it.SavePath)
                        {
                            UseShellExecute = true
                        });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message);
            }
        }

        // Opens Explorer on the downloaded file (or on its folder when the file is not there yet).
        // Returns false when there is nothing to open.
        static bool RevealInExplorer(DownloadItem it)
        {
            string? target =
                File.Exists(it.SavePath)
                    ? it.SavePath
                    : File.Exists(it.TempPath)
                        ? it.TempPath
                        : null;

            if (target != null)
            {
                Process.Start(
                    "explorer.exe",
                    "/select,\"" + target + "\"");

                return true;
            }

            if (!string.IsNullOrEmpty(it.Folder) && Directory.Exists(it.Folder))
            {
                Process.Start(
                    "explorer.exe",
                    "\"" + it.Folder + "\"");

                return true;
            }

            return false;
        }

        // Click on "Save to" in the details panel
        void OpenFolderFromDetails(DownloadItem it)
        {
            try
            {
                if (!RevealInExplorer(it))
                {
                    MessageBox.Show(
                        this,
                        "This folder does not exist (yet):\n" + it.Folder,
                        "FastDM");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message);
            }
        }

        // Click on "Link" in the details panel: open the page in the default browser (web links only)
        void OpenLinkFromDetails(DownloadItem it)
        {
            string url =
                PlaylistBuilder.StripCredentials(
                    it.YtDlp?.Url ?? it.Url ?? "");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
                (u.Scheme != Uri.UriSchemeHttp &&
                 u.Scheme != Uri.UriSchemeHttps))
                return;

            try
            {
                Process.Start(
                    new ProcessStartInfo(u.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message);
            }
        }

        void OnDoubleClick()
        {
            var sel = Sel();

            if (sel.Count == 0)
                return;

            var it = sel[0];

            if (it.DeletedAt.HasValue)
            {
                DownloadAgain();
                return;
            }

            if (it.State ==
                DlState.Completed)
            {
                OpenSelected(false);
            }
            else if (
                it.State ==
                DlState.Downloading)
            {
                PauseSelected();
            }
            else
            {
                ResumeSelected();
            }
        }

        // ---------- Add / Settings ----------
        readonly SemaphoreSlim addGate =
            new SemaphoreSlim(1, 1);

        async void ShowAddDialog(
            string url,
            RequestContext? ctx = null)
        {
            await addGate.WaitAsync();

            try
            {
                await ShowAddDialogCore(url, ctx);
            }
            finally
            {
                addGate.Release();
            }
        }

        // এক্সটেনশনের "Download with FastDM": ডায়ালগ ছাড়া, ডিফল্ট ফোল্ডারে সরাসরি শুরু।
        // (ফোল্ডার লিঙ্ক, ভিডিও পেজ বা yt-dlp সাইটে ইউজারের পছন্দ লাগে, তাই সেসব ক্ষেত্রে নিজস্ব উইন্ডো আসবেই)
        async void QuickAdd(
            string url,
            RequestContext ctx)
        {
            await addGate.WaitAsync();

            try
            {
                await ProcessUrlsAsync(
                    new[] { url },
                    "",
                    settings.DefaultFolder,
                    true,
                    ctx);
            }
            catch (Exception ex)
            {
                lblActive.Text =
                    "Could not add link: " + ex.Message;
            }
            finally
            {
                addGate.Release();
            }
        }

        async Task ShowAddDialogCore(
            string url,
            RequestContext ctx)
        {
            using var dlg =
                new AddUrlForm(
                    url,
                    settings.DefaultFolder);

            Theme.Apply(dlg);

            var result =
                dlg.ShowDialog(this);

            if (result != DialogResult.OK &&
                result != DialogResult.Yes)
                return;

            bool startNow =
                result == DialogResult.OK;

            string[] urls = dlg.Urls;
            string nameText =
                dlg.FileNameText;

            string folder =
                dlg.Folder;

            // A list from a file, or several links that include video-site links: check them in the import
            // window first (one picker per video would take forever and could expand playlists).
            if (dlg.FromFile ||
                (urls.Length >= 2 && urls.Any(YtDlpSites.IsSupported)))
            {
                await StartBatchImportAsync(
                    dlg.RawText,
                    folder);

                return;
            }

            await ProcessUrlsAsync(
                urls,
                nameText,
                folder,
                startNow,
                ctx);
        }

        // Add ডায়ালগ আর এক্সটেনশনের সরাসরি ডাউনলোড, দুই পথেরই মূল কাজ
        async Task ProcessUrlsAsync(
            string[] urls,
            string nameText,
            string folder,
            bool startNow,
            RequestContext ctx)
        {
            // একবারে সর্বোচ্চ কয়টা লিঙ্ক (Preferences → Downloads)
            int maxBatch = Math.Max(1, settings.MaxBatchUrls);

            if (urls.Length > maxBatch)
            {
                MessageBox.Show(
                    this,
                    "Only the first " + maxBatch +
                    " of " + urls.Length +
                    " links will be added.\n(You can change this limit in Preferences → Downloads.)",
                    "FastDM");

                urls = urls.Take(maxBatch).ToArray();
            }

            lblActive.Text =
                "Checking link(s)…";

            var fileUrls =
                new List<string>();

            var folderUris =
                new List<Uri>();

            var mediaItems =
                new List<MediaProbeResult>();

            var ytItems =
                new List<string>();

            foreach (var u in urls)
            {
                // Sites handled by yt-dlp go first. A Facebook or Instagram reel link ends with "/" and used to be
                // taken for a folder, and the folder check also wasted a web request on every YouTube link.
                if (YtDlpSites.IsSupported(u))
                {
                    ytItems.Add(u);
                    continue;
                }

                try
                {
                    using var cts =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(20));

                    var (isFolder, resolved) =
                        await Engine.DetectFolderAsync(
                            new Uri(u),
                            cts.Token);

                    if (isFolder)
                    {
                        folderUris.Add(resolved);
                        continue;
                    }
                }
                catch { }

                if (Engine.IsHttp(u))
                {
                    try
                    {
                        using var mcts =
                            new CancellationTokenSource(
                                TimeSpan.FromSeconds(20));

                        var media =
                            await MediaDetector.ProbeAsync(
                                new Uri(u),
                                mcts.Token);

                        if (media.Kind ==
                                MediaKind.Hls ||
                            media.Kind ==
                                MediaKind.Dash ||
                            media.Kind ==
                                MediaKind.Page)
                        {
                            mediaItems.Add(media);
                            continue;
                        }
                    }
                    catch { }
                }

                fileUrls.Add(u);
            }

            // ফাইল লিঙ্ক
            lblActive.Text =
                "Fetching file info…";

            int addedCount = 0;
            int skippedPages = 0;

            foreach (var u in fileUrls)
            {
                var it = new DownloadItem
                {
                    Url = u,
                    Folder = folder,
                    Referer = ctx?.Referer,
                    UserAgent = ctx?.UserAgent,
                    FileName =
                        fileUrls.Count == 1 &&
                        folderUris.Count == 0 &&
                        nameText.Length > 0
                            ? Engine.Sanitize(
                                nameText)
                            : ""
                };

                bool retry = true;

                while (retry)
                {
                    retry = false;

                    try
                    {
                        using var cts =
                            new CancellationTokenSource(
                                TimeSpan.FromSeconds(25));

                        await Engine.ProbeAsync(
                            it,
                            cts.Token);
                    }
                    catch (AuthRequiredException)
                    {
                        if (CredentialForm.Prompt(
                                this,
                                new Uri(u)))
                        {
                            retry = true;
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(
                        it.FileName))
                {
                    it.FileName =
                        Engine.NameFromUrl(u);
                }

                // "Do not download web pages": উত্তর HTML পেজ হলে বাদ (.html লিঙ্ক ছাড়া)
                if (settings.SkipWebPages &&
                    string.Equals(
                        it.ContentType,
                        "text/html",
                        StringComparison.OrdinalIgnoreCase) &&
                    !IsHtmlName(
                        u))
                {
                    skippedPages++;
                    continue;
                }

                it.Folder =
                    SmartFolder(
                        folder,
                        it.FileName,
                        u);

                it.FileName =
                    UniqueName(
                        it.Folder,
                        it.FileName);

                it.State =
                    startNow
                        ? DlState.Queued
                        : DlState.Paused;

                items.Add(it);
                selectNewId = it.Id;
                addedCount++;
            }

            if (skippedPages > 0)
            {
                lblActive.Text =
                    "Skipped " + skippedPages +
                    " web page link(s). (Preferences → Downloads → Do not download web pages)";
            }

            if (addedCount > 0 &&
                settings.NotifyAdded)
            {
                Notify(
                    "Download added",
                    addedCount == 1
                        ? items[items.Count - 1].FileName
                        : addedCount + " downloads added");
            }

            MarkChanged();
            SaveState();

            // ফোল্ডার
            foreach (var fu in folderUris)
                AddFolder(
                    fu,
                    startNow);

            // মিডিয়া
            foreach (var mi in mediaItems)
                await AddMediaAsync(
                    mi,
                    folder,
                    startNow);

            // yt-dlp
            foreach (var yu in ytItems)
                await AddYtDlpAsync(
                    yu,
                    folder,
                    startNow);
        }

        // Drag-and-drop of a list file
        async void ImportListFile(string path)
        {
            await addGate.WaitAsync();

            try
            {
                if (new FileInfo(path).Length > 5 * 1024 * 1024)
                {
                    MessageBox.Show(
                        this,
                        "This file is too big for a list of links (limit 5 MB).",
                        "FastDM");

                    return;
                }

                await StartBatchImportAsync(
                    File.ReadAllText(path),
                    settings.DefaultFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "FastDM");
            }
            finally
            {
                addGate.Release();
            }
        }

        // Check the list line by line, show the preview, then add what the user ticked.
        async Task StartBatchImportAsync(
            string text,
            string folder)
        {
            var listed =
                items
                    .Select(i => i.YtDlp?.Url ?? i.Url ?? "")
                    .ToList();

            var rows =
                BatchImport.Parse(
                    text,
                    listed);

            if (rows.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No links were found in this text.",
                    "FastDM");

                return;
            }

            using var dlg =
                new BatchImportForm(
                    rows,
                    folder,
                    settings.MaxBatchUrls);

            Theme.Apply(dlg);

            var result =
                dlg.ShowDialog(this);

            if (result != DialogResult.OK &&
                result != DialogResult.Yes)
                return;

            await AddBatchAsync(
                dlg.SelectedRows,
                dlg.Quality,
                dlg.FoldersAutomatically,
                dlg.SaveFolder,
                result == DialogResult.OK);
        }

        async Task AddBatchAsync(
            List<BatchRow> chosenRows,
            BatchQuality quality,
            bool foldersAutomatically,
            string folder,
            bool startNow)
        {
            // never more than the limit from Preferences > Downloads
            int cap = Math.Max(1, settings.MaxBatchUrls);
            var chosen = chosenRows.Take(cap).ToList();
            int overCap = chosenRows.Count - chosen.Count;

            int videos = 0;
            var folderUris = new List<Uri>();
            var otherUrls = new List<string>();

            foreach (var r in chosen)
            {
                if (r.Kind == BatchKind.Video)
                {
                    AddBatchVideo(r.Text, quality, folder, startNow);
                    videos++;
                }
                else if (r.Kind == BatchKind.Folder)
                {
                    if (Uri.TryCreate(r.Text, UriKind.Absolute, out var fu))
                        folderUris.Add(fu);
                }
                else if (r.Kind == BatchKind.File)
                {
                    otherUrls.Add(r.Text);
                }
            }

            MarkChanged();
            SaveState();

            // files and other links: the normal path (size, name, duplicates)
            int before = items.Count;

            if (otherUrls.Count > 0)
            {
                await ProcessUrlsAsync(
                    otherUrls.ToArray(),
                    "",
                    folder,
                    startNow,
                    new RequestContext());
            }

            int others = items.Count - before;

            // folders
            int folderFiles = 0;
            int folderNo = 0;

            foreach (var fu in folderUris)
            {
                folderNo++;

                if (foldersAutomatically)
                {
                    lblActive.Text =
                        "Folder " + folderNo + " of " + folderUris.Count + "...";

                    folderFiles +=
                        await AddFolderAllAsync(
                            fu,
                            folder,
                            startNow);
                }
                else
                {
                    AddFolder(
                        fu,
                        startNow);
                }
            }

            MarkChanged();
            SaveState();

            var parts = new List<string>();
            if (videos > 0) parts.Add(videos + (videos == 1 ? " video" : " videos"));
            if (folderFiles > 0) parts.Add(folderFiles + (folderFiles == 1 ? " file from folders" : " files from folders"));
            if (others > 0) parts.Add(others + (others == 1 ? " other file" : " other files"));

            string text =
                parts.Count > 0
                    ? "Added: " + string.Join(", ", parts) + "."
                    : "Nothing was added.";

            if (overCap > 0)
                text += " " + overCap + " more were left out (limit " + cap + ").";

            lblActive.Text = text;
        }

        // A video in a batch: no lookup first. The file is named after the title when it is downloaded.
        void AddBatchVideo(
            string url,
            BatchQuality quality,
            string folder,
            bool startNow)
        {
            string label = "video";

            if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                string host =
                    u.Host.StartsWith("www.")
                        ? u.Host.Substring(4)
                        : u.Host;

                string tail =
                    BatchImport.YouTubeId(u) ??
                    u.AbsolutePath.Trim('/').Split('/').LastOrDefault() ??
                    "";

                label = host + (tail.Length > 0 ? " " + tail : "");
            }

            string ext =
                quality.AudioOnly
                    ? "." + quality.AudioFormat
                    : ".mp4";

            var it = new DownloadItem
            {
                Url = url,
                Folder = folder,
                FileName = Engine.Sanitize(label) + ext,
                YtDlp = new YtDlpSpec
                {
                    Url = url,
                    Format = quality.Format,
                    AudioOnly = quality.AudioOnly,
                    AudioFormat = quality.AudioFormat,
                    NameFromTitle = true
                },
                State =
                    startNow
                        ? DlState.Queued
                        : DlState.Paused
            };

            items.Add(it);
            selectNewId = it.Id;
        }

        // A whole folder, without the tree window: every file goes into <folder>\<name of the remote folder>.
        // Returns how many files were added.
        async Task<int> AddFolderAllAsync(
            Uri uri,
            string baseFolder,
            bool startNow)
        {
            RemoteNode? root = null;

            while (root == null)
            {
                try
                {
                    lblActive.Text = "Scanning " + uri.Host + "...";

                    using var cts =
                        new CancellationTokenSource(
                            TimeSpan.FromMinutes(10));

                    root =
                        await RemoteLister.ScanAsync(
                            uri,
                            new Progress<int>(n =>
                                lblActive.Text =
                                    "Scanning " + uri.Host + "... " + n + " file(s) found"),
                            cts.Token);
                }
                catch (AuthRequiredException)
                {
                    if (!CredentialForm.Prompt(this, uri))
                        return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Could not scan this folder:\n" + uri + "\n\n" +
                        ex.GetBaseException().Message,
                        "FastDM",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return 0;
                }
            }

            string leaf =
                Uri.UnescapeDataString(uri.AbsolutePath)
                   .TrimEnd('/');

            leaf = leaf.Substring(leaf.LastIndexOf('/') + 1);

            if (leaf.Length == 0)
                leaf = uri.Host;

            string target =
                Path.Combine(
                    baseFolder,
                    SafeFolderName(leaf));

            int count = 0;
            AddFolderNode(root, target, startNow, ref count);

            return count;
        }

        static string SafeFolderName(string name)
        {
            string s = Engine.Sanitize(name).TrimEnd('.', ' ');
            return s.Length == 0 ? "_" : s;
        }

        void AddFolderNode(
            RemoteNode node,
            string dir,
            bool startNow,
            ref int count)
        {
            foreach (var c in node.Children)
            {
                if (c.IsDir)
                {
                    AddFolderNode(
                        c,
                        Path.Combine(dir, SafeFolderName(c.Name)),
                        startNow,
                        ref count);

                    continue;
                }

                if (string.IsNullOrEmpty(c.Url))
                    continue;

                var it = new DownloadItem
                {
                    Url = c.Url,
                    Folder = dir,
                    FileName =
                        UniqueName(
                            dir,
                            Engine.Sanitize(c.Name)),
                    TotalBytes = c.Size,
                    NeedsProbe = Engine.IsHttp(c.Url),
                    State =
                        startNow
                            ? DlState.Queued
                            : DlState.Paused
                };

                items.Add(it);
                selectNewId = it.Id;
                count++;
            }
        }

        async Task AddMediaAsync(
            MediaProbeResult r,
            string saveFolder,
            bool startNow)
        {
            string url = r.Url;
            string? referer = null;
            MediaKind kind = r.Kind;

            if (kind == MediaKind.Page)
            {
                MediaCandidate pick;

                if (r.Candidates.Count == 1)
                {
                    pick = r.Candidates[0];
                }
                else
                {
                    using var cf =
                        new CandidateForm(
                            r.Candidates);

                    Theme.Apply(cf);

                    if (cf.ShowDialog(this) !=
                        DialogResult.OK)
                    {
                        return;
                    }

                    if (cf.Picked is not MediaCandidate candidate) return;
                    pick = candidate;
                }

                referer = r.Url;

                if (pick.Kind ==
                    MediaKind.Direct)
                {
                    await AddDirectCandidateAsync(
                        pick.Url,
                        saveFolder,
                        startNow);

                    return;
                }

                url = pick.Url;
                kind = pick.Kind;
            }

            MediaOptions? opts = null;

            lblActive.Text =
                "Reading stream info…";

            while (opts == null)
            {
                try
                {
                    using var cts =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(25));

                    opts =
                        await MediaParser.LoadAsync(
                            url,
                            kind,
                            referer,
                            r.Title,
                            cts.Token);
                }
                catch (AuthRequiredException)
                {
                    if (!CredentialForm.Prompt(
                            this,
                            new Uri(url)))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Could not read this stream.\n\n" +
                        ex.GetBaseException().Message,
                        "Video",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }
            }

            using var pf =
                new MediaPickerForm(
                    opts,
                    saveFolder);

            Theme.Apply(pf);

            if (pf.ShowDialog(this) !=
                DialogResult.OK)
                return;

            string folder = pf.SaveFolder;

            var spec = pf.Spec;
            if (spec == null) return;
            string ext =
                spec.AudioOnly
                    ? ".m4a"
                    : ".mp4";

            var it = new DownloadItem
            {
                Url = url,
                Folder = folder,
                FileName =
                    UniqueName(
                        folder,
                        Engine.Sanitize(
                            pf.FileName) +
                        ext),
                Stream = spec,
                State =
                    startNow
                        ? DlState.Queued
                        : DlState.Paused
            };

            items.Add(it);
            selectNewId = it.Id;
            MarkChanged();
            SaveState();
        }

        async Task AddYtDlpAsync(
            string url,
            string saveFolder,
            bool startNow)
        {
            if (YtDlpLocator.Find() == null)
            {
                MessageBox.Show(
                    this,
                    "yt-dlp.exe was not found.\nPut yt-dlp.exe (and deno.exe) in the Tools folder next to the app.",
                    "Video",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            string path =
                new Uri(url)
                    .AbsolutePath
                    .ToLowerInvariant();

            if (path.StartsWith(
                    "/playlist"))
            {
                MessageBox.Show(
                    this,
                    "Playlist links are not supported yet.\nOpen a single video link instead.",
                    "Video",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            lblActive.Text =
                "Reading video info…";

            UseWaitCursor = true;

            var readWatch = Stopwatch.StartNew();

            using var readTick =
                new System.Windows.Forms.Timer { Interval = 1000 };

            readTick.Tick += (s, e) =>
                lblActive.Text =
                    "Reading video info\u2026 " +
                    (int)readWatch.Elapsed.TotalSeconds +
                    " s";

            readTick.Start();

            YtInfo info;

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(90));

                info =
                    await YtDlpInfo.LoadAsync(
                        url,
                        cts.Token);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show(
                    this,
                    "Reading the video info took too long.",
                    "Video",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }
            catch (Exception ex)
            {
                string hint =
                    YtDlpCookies.HintFor(ex.GetBaseException().Message) ??
                    (UpdateChecker.IsPackaged
                        ? "\n\nIf this keeps happening, a newer app version (with an updated yt-dlp) may fix it."
                        : "\n\nTry Settings → Update yt-dlp.");

                MessageBox.Show(
                    this,
                    "Could not read this video.\n\n" +
                    ex.GetBaseException().Message +
                    hint,
                    "Video",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }
            finally
            {
                readTick.Stop();
                UseWaitCursor = false;
            }

            using var pf =
                new YtPickerForm(
                    info,
                    url,
                    saveFolder);

            Theme.Apply(pf);

            if (pf.ShowDialog(this) !=
                DialogResult.OK)
                return;

            string folder =
                pf.SaveFolder;

            var spec = pf.Spec;
            if (spec == null) return;
            string ext =
                spec.AudioOnly
                    ? "." + pf.Spec.AudioFormat
                    : ".mp4";

            var it = new DownloadItem
            {
                Url = url,
                Folder = folder,
                FileName =
                    UniqueName(
                        folder,
                        pf.Spec.BaseName +
                        ext),
                YtDlp = pf.Spec,
                State =
                    startNow
                        ? DlState.Queued
                        : DlState.Paused
            };

            items.Add(it);
            selectNewId = it.Id;

            MarkChanged();
            SaveState();
        }

        async Task AddDirectCandidateAsync(
            string url,
            string saveFolder,
            bool startNow)
        {
            var it = new DownloadItem
            {
                Url = url,
                Folder = saveFolder,
                FileName = ""
            };

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(25));

                await Engine.ProbeAsync(
                    it,
                    cts.Token);
            }
            catch { }

            if (string.IsNullOrWhiteSpace(
                    it.FileName))
            {
                it.FileName =
                    Engine.NameFromUrl(url);
            }

            it.FileName =
                UniqueName(
                    saveFolder,
                    it.FileName);

            it.State =
                startNow
                    ? DlState.Queued
                    : DlState.Paused;

            items.Add(it);
            selectNewId = it.Id;

            MarkChanged();
            SaveState();
        }

        void AddFolder(
            Uri uri,
            bool startNow,
            bool playlistMode = false)
        {
            using var f =
                new FolderDownloadForm(
                    uri,
                    settings.DefaultFolder,
                    settings.MaxSimultaneous,
                    playlistMode);

            Theme.Apply(f);

            if (f.ShowDialog(this) !=
                DialogResult.OK)
                return;

            settings.MaxSimultaneous =
                f.Simultaneous;

            var localPaths =
                new List<string>();

            foreach (var pf in f.Files)
            {
                string dir =
                    pf.RelDir.Length == 0
                        ? f.TargetFolder
                        : Path.Combine(
                            f.TargetFolder,
                            pf.RelDir);

                var it = new DownloadItem
                {
                    Url = pf.Url,
                    Folder = dir,
                    FileName =
                        UniqueName(
                            dir,
                            Engine.Sanitize(
                                pf.Name)),
                    TotalBytes = pf.Size,
                    NeedsProbe =
                        Engine.IsHttp(pf.Url),
                    State =
                        startNow
                            ? DlState.Queued
                            : DlState.Paused
                };

                items.Add(it);
                selectNewId = it.Id;

                localPaths.Add(
                    Path.Combine(
                        pf.RelDir,
                        it.FileName));
            }

            MarkChanged();
            SaveState();

            if (f.SaveLocalPlaylist)
                WriteLocalPlaylist(
                    f.TargetFolder,
                    localPaths);
        }

        // ডাউনলোড ফোল্ডারের ভেতর <ফোল্ডারের নাম>.m3u8 (রিলেটিভ পাথ, ফাইল নামলেই VLC-তে চলবে)
        void WriteLocalPlaylist(
            string targetFolder,
            List<string> relPaths)
        {
            try
            {
                var result =
                    PlaylistBuilder.BuildLocal(
                        relPaths);

                if (result.Count == 0)
                {
                    lblActive.Text =
                        "No video or audio files selected: local playlist not created.";
                    return;
                }

                Directory.CreateDirectory(
                    targetFolder);

                string leaf =
                    Engine.Sanitize(
                        Path.GetFileName(
                            targetFolder.TrimEnd(
                                '\\',
                                '/')));

                if (string.IsNullOrWhiteSpace(leaf))
                    leaf = "playlist";

                string file =
                    Path.Combine(
                        targetFolder,
                        leaf + ".m3u8");

                PlaylistBuilder.Save(
                    file,
                    result.Text);

                lblActive.Text =
                    "Local playlist saved: " +
                    file;
            }
            catch (Exception ex)
            {
                lblActive.Text =
                    "Could not save the local playlist: " +
                    ex.Message;
            }
        }

        // এক্সটেনশনের "Create playlist": Add ডায়ালগ ছাড়া সরাসরি ফোল্ডার উইন্ডো (প্লেলিস্ট মোডে)
        async void OpenFolderForPlaylist(
            string url)
        {
            await addGate.WaitAsync();

            try
            {
                if (!Uri.TryCreate(
                        url,
                        UriKind.Absolute,
                        out var u))
                    return;

                Uri folder = u;

                if (Engine.IsHttp(url))
                {
                    using var cts =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(20));

                    var (isFolder, resolved) =
                        await Engine.DetectFolderAsync(
                            u,
                            cts.Token);

                    if (!isFolder)
                    {
                        MessageBox.Show(
                            this,
                            "This link does not look like a folder (directory listing), so a playlist cannot be made from it.",
                            "Playlist",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        return;
                    }

                    folder = resolved;
                }
                else if (!u.AbsolutePath.EndsWith("/"))
                {
                    folder =
                        new Uri(
                            u.GetLeftPart(
                                UriPartial.Path) +
                            "/");
                }

                AddFolder(
                    folder,
                    true,
                    true);
            }
            catch (Exception ex)
            {
                lblActive.Text =
                    "Could not open the folder: " +
                    ex.Message;
            }
            finally
            {
                addGate.Release();
            }
        }

        static bool IsHtmlName(
            string url)
        {
            try
            {
                string ext =
                    Path.GetExtension(
                        new Uri(url).AbsolutePath);

                return ext.Equals(
                           ".html",
                           StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(
                           ".htm",
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // "Suggest folders": শুধু ডিফল্ট ফোল্ডারে সেভ করলেই সাবফোল্ডার বসে (ইউজার নিজে ফোল্ডার বাছলে তার পছন্দই চূড়ান্ত)
        string SmartFolder(
            string folder,
            string fileName,
            string url)
        {
            if (!settings.SuggestByType &&
                !settings.SuggestByHost)
                return folder;

            try
            {
                string a =
                    Path.GetFullPath(
                        folder).TrimEnd('\\', '/');

                string b =
                    Path.GetFullPath(
                        settings.DefaultFolder).TrimEnd('\\', '/');

                if (!a.Equals(
                        b,
                        StringComparison.OrdinalIgnoreCase))
                    return folder;

                string f = folder;

                if (settings.SuggestByType)
                    f = Path.Combine(
                        f,
                        FileCategories.Of(
                            fileName));

                if (settings.SuggestByHost &&
                    Uri.TryCreate(
                        url,
                        UriKind.Absolute,
                        out var u) &&
                    u.Host.Length > 0)
                    f = Path.Combine(
                        f,
                        Engine.Sanitize(
                            u.Host));

                return f;
            }
            catch
            {
                return folder;
            }
        }

        string UniqueName(
            string folder,
            string name)
        {
            string stem =
                Path.GetFileNameWithoutExtension(
                    name);

            string ext =
                Path.GetExtension(name);

            string cand = name;
            int n = 1;

            // একই নামের ফাইল থাকলে Preferences অনুযায়ী: Rename / Overwrite / Ask
            bool replaceExisting = false;

            if (File.Exists(
                    Path.Combine(
                        folder,
                        name)))
            {
                if (settings.FileExists ==
                    FileExistsAction.Overwrite)
                {
                    replaceExisting = true;
                }
                else if (settings.FileExists ==
                         FileExistsAction.Ask)
                {
                    replaceExisting =
                        MessageBox.Show(
                            this,
                            "\"" + name + "\" already exists in this folder.\n\n" +
                            "Yes = replace it\n" +
                            "No = keep both (the new file gets a different name)",
                            "File exists",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question) ==
                        DialogResult.Yes;
                }
            }

            while (
                (File.Exists(
                    Path.Combine(
                        folder,
                        cand)) &&
                 !(replaceExisting &&
                   cand == name)) ||

                File.Exists(
                    Path.Combine(
                        folder,
                        cand) +
                    ".part") ||

                items.Any(i =>
                    i.Folder == folder &&
                    i.FileName.Equals(
                        cand,
                        StringComparison.OrdinalIgnoreCase)))
            {
                cand =
                    stem +
                    " (" +
                    (n++) +
                    ")" +
                    ext;
            }

            return cand;
        }

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

        void ToggleDetails()
        {
            settings.ShowDetails =
                !settings.ShowDetails;

            details.Visible =
                settings.ShowDetails;

            details.SetItem(
                Sel().FirstOrDefault());

            SaveState();
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
        }

        void RunAfterAllAction()
        {
            var act = afterAll;

            if (act ==
                PostAction.None)
                return;

            afterAll =
                PostAction.None;

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

                    MessageBox.Show(
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

        // "Compact view of downloads list": ছোট সারি
        void ApplyCompactView()
        {
            if (lv?.SmallImageList == null)
                return;

            lv.SmallImageList.ImageSize =
                new Size(
                    1,
                    settings.CompactView
                        ? 26
                        : 34);

            lv.Invalidate();
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
                    "Mode: " +
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

            SuspendLayout();

            BackColor = p.Window;
            ForeColor = p.Text;

            lv.BackColor = p.Window;
            lv.ForeColor = p.Text;

            sidebar.BackColor = p.SideBg;

            Theme.StyleStrip(tools);
            Theme.StyleStrip(statusBar);
            if (lv.ContextMenuStrip != null) Theme.StyleStrip(lv.ContextMenuStrip);
            Theme.StyleStrip(trayMenu);
            Theme.StyleStrip(modeMenu.DropDown);
            Theme.StyleStrip(doneMenu.DropDown);

            lblActive.ForeColor =
                p.SubText;

            lblSpeed.ForeColor =
                p.SubText;

            lblLimit.ForeColor =
                p.SubText;

            lblSched.ForeColor =
                p.SubText;

            details?.Invalidate();

            RefreshToolIcons();

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
            catch { }
        }

        // নামের কলাম বাদে শেষ কলাম বাকি জায়গা ভরবে
        void FitLastColumn()
        {
            if (lv == null ||
                lv.Columns.Count == 0 ||
                lv.ClientSize.Width <= 0)
                return;

            int used = 0;

            for (int i = 0;
                 i < lv.Columns.Count - 1;
                 i++)
            {
                used +=
                    lv.Columns[i].Width;
            }

            int w =
                Math.Max(
                    120,
                    lv.ClientSize.Width - used);

            var last =
                lv.Columns[
                    lv.Columns.Count - 1];

            if (last.Width != w)
                last.Width = w;
        }

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

        // ---------- ব্রাউজার এক্সটেনশন bridge ----------
        void StartBridge()
        {
            try
            {
                if (!settings.BridgeEnabled ||
                    bridge != null)
                    return;

                var b =
                    new BridgeServer(
                        new BridgeHostAdapter(this));

                if (b.Start())
                    bridge = b;
                else
                    b.Dispose();
            }
            catch
            {
                bridge = null;
            }
        }

        void StopBridge()
        {
            try
            {
                bridge?.Dispose();
            }
            catch { }

            bridge = null;
        }

        sealed class BridgeHostAdapter : IBridgeHost
        {
            readonly MainForm f;

            public BridgeHostAdapter(MainForm form)
            {
                f = form;
            }

            public AppSettings BridgeSettings =>
                f.settings;

            public Task<bool> AskPairAsync(
                string origin)
            {
                var tcs =
                    new TaskCompletionSource<bool>();

                try
                {
                    f.BeginInvoke(
                        new Action(() =>
                        {
                            f.RestoreFromTray();

                            var r =
                                MessageBox.Show(
                                    f,
                                    "A browser extension wants to connect to FastDM.\n\nOrigin: " +
                                    origin +
                                    "\n\nAllow it to send downloads to FastDM?",
                                    "FastDM — browser extension",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question,
                                    MessageBoxDefaultButton.Button2);

                            tcs.TrySetResult(
                                r == DialogResult.Yes);
                        }));
                }
                catch
                {
                    tcs.TrySetResult(false);
                }

                return tcs.Task;
            }

            public void ExternalAdd(
                BridgeAddRequest add)
            {
                try
                {
                    var ctx = new RequestContext
                    {
                        Referer = add.Referer,
                        UserAgent = add.UserAgent
                    };

                    // কুকি আগে জারে, তারপর ডাউনলোড/ডায়ালগ
                    if (add.Cookies != null) Engine.AddCookies(add.Cookies);

                    bool direct =
                        string.Equals(
                            add.Mode,
                            "download",
                            StringComparison.OrdinalIgnoreCase);

                    bool playlist =
                        string.Equals(
                            add.Mode,
                            "playlist",
                            StringComparison.OrdinalIgnoreCase);

                    f.BeginInvoke(
                        new Action(() =>
                        {
                            if (direct)
                            {
                                f.QuickAdd(add.Url, ctx);
                            }
                            else if (playlist)
                            {
                                f.RestoreFromTray();
                                f.OpenFolderForPlaylist(add.Url);
                            }
                            else
                            {
                                f.RestoreFromTray();
                                f.ShowAddDialog(add.Url, ctx);
                            }
                        }));
                }
                catch { }
            }

            public List<BridgeTaskInfo> GetTasks()
            {
                try
                {
                    return (List<BridgeTaskInfo>)f.Invoke(
                        new Func<List<BridgeTaskInfo>>(
                            () =>
                                f.items
                                 .OrderByDescending(
                                     i => i.Added)
                                 .Take(30)
                                 .Select(
                                     i =>
                                         new BridgeTaskInfo
                                         {
                                             Id = i.Id,
                                             Name = i.FileName,
                                             State =
                                                 i.State ==
                                                     DlState.Completed
                                                     ? "Complete"
                                                     : i.State.ToString(),
                                             Percent =
                                                 i.Percent,
                                             Speed =
                                                 i.Speed,
                                             Size =
                                                 i.TotalBytes,
                                             Downloaded =
                                                 i.Downloaded,
                                             Eta =
                                                 Eta(i)
                                         })
                                 .ToList()));
                }
                catch
                {
                    return new List<BridgeTaskInfo>();
                }
            }

            public bool TaskAction(
                string id,
                string action)
            {
                try
                {
                    return (bool)f.Invoke(
                        new Func<bool>(() =>
                        {
                            var it =
                                f.items.FirstOrDefault(
                                    x => x.Id == id);

                            if (it == null)
                                return false;

                            if (action == "pause")
                            {
                                if (it.State ==
                                    DlState.Downloading)
                                {
                                    it.Cts?.Cancel();
                                }
                                else if (
                                    it.State ==
                                    DlState.Queued)
                                {
                                    it.State =
                                        DlState.Paused;
                                }
                            }
                            else if (
                                action == "resume")
                            {
                                if (
                                    it.State ==
                                        DlState.Paused ||
                                    it.State ==
                                        DlState.Error)
                                {
                                    it.State =
                                        DlState.Queued;

                                    it.Error = null;
                                }
                            }

                            f.MarkChanged();

                            return true;
                        }));
                }
                catch
                {
                    return false;
                }
            }

            public void SaveSettings()
            {
                try
                {
                    f.BeginInvoke(
                        new Action(
                            f.SaveState));
                }
                catch { }
            }
        }

        // ---------- fastdm:// কমান্ড ----------
        void StartPipeListener()
        {
            pipeCts =
                new CancellationTokenSource();

            var token =
                pipeCts.Token;

            string name =
                ProtocolHandler.PipeName();

            _ = Task.Run(
                async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            using var server =
                                new NamedPipeServerStream(
                                    name,
                                    PipeDirection.In,
                                    1,
                                    PipeTransmissionMode.Byte,
                                    PipeOptions.Asynchronous);

                            await server
                                .WaitForConnectionAsync(token);

                            using var sr =
                                new StreamReader(
                                    server,
                                    Encoding.UTF8);

                            string? line =
                                await sr.ReadLineAsync();

                            if (!string.IsNullOrWhiteSpace(line) &&
                                IsHandleCreated &&
                                !IsDisposed)
                            {
                                BeginInvoke(
                                    new Action(
                                        () =>
                                            HandleProtocolCommand(
                                                line)));
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch
                        {
                            try
                            {
                                await Task.Delay(
                                    500,
                                    token);
                            }
                            catch
                            {
                                break;
                            }
                        }
                    }
                });
        }

        void HandleProtocolCommand(
            string raw)
        {
            var cmd =
                ProtocolHandler.Parse(raw);

            if (cmd == null)
                return;

            RestoreFromTray();

            if (cmd.Action == "add" &&
                !string.IsNullOrEmpty(cmd.Url))
            {
                ShowAddDialog(
                    cmd.Url);
            }
            else if (cmd.Action == "playlist" &&
                     !string.IsNullOrEmpty(cmd.Url))
            {
                // fastdm:// যেকোনো ওয়েবপেজ ট্রিগার করতে পারে, তাই স্ক্যানের আগে ইউজারের হ্যাঁ লাগবে
                var ask =
                    MessageBox.Show(
                        this,
                        "A web link asks FastDM to scan this folder and make a playlist:\n\n" +
                        cmd.Url +
                        "\n\nContinue?",
                        "FastDM",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);

                if (ask == DialogResult.Yes)
                    OpenFolderForPlaylist(
                        cmd.Url);
            }
        }

        void SetupSingleInstanceListener()
        {
            try
            {
                showEvent =
                    new EventWaitHandle(
                        false,
                        EventResetMode.AutoReset,
                        @"Local\FastDM.Show");

                regWait =
                    ThreadPool.RegisterWaitForSingleObject(
                        showEvent,
                        (state, timedOut) =>
                        {
                            try
                            {
                                if (!IsDisposed &&
                                    IsHandleCreated)
                                {
                                    BeginInvoke(
                                        new Action(
                                            RestoreFromTray));
                                }
                            }
                            catch { }
                        },
                        null,
                        Timeout.Infinite,
                        false);
            }
            catch { }
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

        // ---------- সেভ / লোড ----------
        // Set by LoadState when state.json was damaged or newer: shown once after the window opens.
        string? startupNotice;

        // True when a damaged state.json could not be set aside: saving would destroy it, so we do not save.
        bool stateWriteBlocked;

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

        void SaveState()
        {
            lastSave =
                DateTime.UtcNow;

            if (stateWriteBlocked)
                return;

            try
            {
                Directory.CreateDirectory(
                    dataDir);

                var json =
                    JsonSerializer.Serialize(
                        new AppData
                        {
                            SchemaVersion =
                                StateStore.CurrentSchema,
                            Settings =
                                settings,
                            Items =
                                items.ToList(),
                            Deleted =
                                deleted.ToList()
                        },
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                StateStore.Write(
                    DataFile,
                    json);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "Could not save state: " +
                    ex.Message);
            }
        }

        void LoadState()
        {
            AppData? data = null;

            try
            {
                var read =
                    StateStore.Read<AppData>(
                        DataFile,
                        text => JsonSerializer.Deserialize<AppData>(text));

                data = read.Data;

                if (read.Notice != null)
                    startupNotice = read.Notice;

                if (read.KeepBlocked)
                    stateWriteBlocked = true;
            }
            catch { }

            if (data == null)
                return;

            try
            {
                // Saved by a newer FastDM: keep an untouched copy, this version may drop settings it does not know.
                if (data.SchemaVersion > StateStore.CurrentSchema)
                {
                    string? kept =
                        StateStore.KeepNewerCopy(
                            DataFile,
                            data.SchemaVersion);

                    startupNotice =
                        (startupNotice == null ? "" : startupNotice + "\n\n") +
                        "Your saved settings come from a newer version of FastDM. This version may not know all of them. " +
                        "Please update FastDM." +
                        (kept == null ? "" : "\n\nAn untouched copy was kept here:\n" + kept);
                }

                // Layout changes between versions go here, one step at a time.
                // v0 (no SchemaVersion) -> v1: same layout, nothing to convert.
                // v1 -> v2: files deleted from the disk now move to the Deleted list by themselves
                bool upgradeToV2 = data.SchemaVersion < 2;

                if (data.SchemaVersion < StateStore.CurrentSchema)
                    data.SchemaVersion = StateStore.CurrentSchema;

                if (data.Settings != null)
                    settings =
                        data.Settings;

                if (upgradeToV2)
                    settings.AutoRemoveMissing = true;

                foreach (var d in data.Deleted ?? new List<DownloadItem>())
                {
                    if (!d.DeletedAt.HasValue)
                        d.DeletedAt = d.Added;

                    if (d.State == DlState.Downloading)
                        d.State = DlState.Paused;

                    deleted.Add(d);
                }

                // পুরোনো একক গতি/কানেকশন সেটিং → High মোডে (আগের আচরণ হুবহু থাকে)
                if (!settings.TrafficMigrated)
                {
                    settings.HighProfile =
                        new TrafficProfile(
                            settings.SpeedLimitKBps,
                            200,
                            Math.Clamp(settings.Connections, 1, 16),
                            Math.Clamp(settings.MaxSimultaneous, 1, 10));

                    settings.ActiveMode =
                        TrafficMode.High;

                    settings.TrafficMigrated =
                        true;
                }

                // পুরোনো "Show notifications" বন্ধ থাকলে নতুন তিনটা সুইচও বন্ধ
                if (!settings.ShowNotifications)
                {
                    settings.NotifyAdded = false;
                    settings.NotifyCompleted = false;
                    settings.NotifyFailed = false;
                    settings.ShowNotifications = true;
                }

                foreach (var it in
                    data.Items ??
                    new List<DownloadItem>())
                {
                    if (it.State ==
                        DlState.Downloading)
                    {
                        it.State =
                            DlState.Paused;
                    }

                    items.Add(it);
                }
            }
            catch { }
        }
    }
}