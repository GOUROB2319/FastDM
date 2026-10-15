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
    // MainForm part: window layout, toolbar, sidebar, details panel, view options, drawing and context menu.
    public partial class MainForm
    {
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
                    {
                        try
                        {
                            await UpdateChecker
                                .CheckAndShowAsync(this, false);
                        }
                        catch (OperationCanceledException)
                        {
                            AppLog.Write("MainForm.BuildUI Updates button: cancelled");
                        }
                        catch (Exception exGuard)
                        {
                            Program.ReportError("MainForm.BuildUI Updates button", exGuard);
                        }
                    },
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
            if (e.Item is not ListViewItem item || item.Tag is not DownloadItem it) return;
            var g = e.Graphics;

            var r = new Rectangle(
                e.Bounds.X,
                e.Bounds.Y,
                lv.Columns[e.ColumnIndex].Width,
                e.Bounds.Height);

            Color back =
                item.Selected
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
    }
}
