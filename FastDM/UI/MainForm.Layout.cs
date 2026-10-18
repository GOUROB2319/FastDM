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
            Text = "FastDM — Download Manager";
            ClientSize = new Size(1100, 640);
            MinimumSize = new Size(520, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.CreateFont(Theme.FontBody);
            KeyPreview = true;
            AllowDrop = true;

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            DragLeave += OnDragLeave;
            KeyDown += OnMainKeyDown;

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
                AccessibleName = "Downloads",
                AccessibleDescription = "Download list. Use the arrow keys to select an item. Press Delete to remove the selection.",
                AccessibleRole = AccessibleRole.List,
                AllowDrop = true,
                SmallImageList = new ImageList
                {
                    ImageSize = new Size(1, Theme.Scale(this, 34))
                }
            };

            lv.Columns.Add("Name", 300);
            lv.Columns.Add("Size", 90);
            lv.Columns.Add("Progress", 170);
            lv.Columns.Add("Status", 100);
            lv.Columns.Add("Speed", 95);
            lv.Columns.Add("ETA", 80);
            lv.Columns.Add("Added", 150);
            lv.ClientSizeChanged += (s, e) => FitLastColumn();

            lv.DrawColumnHeader += DrawHeader;
            lv.DrawItem += (s, e) => { };
            lv.DrawSubItem += DrawSub;
            lv.ColumnClick += OnColumnClick;
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
            lv.DragLeave += OnDragLeave;

            lv.SelectedIndexChanged += (s, e) =>
                details?.SetItem(
                    Sel().FirstOrDefault());

            listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.P.Window };
            var listLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 2
            };
            listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(this, 44)));
            listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var searchHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 6, 12, 6),
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.P.Window
            };
            searchHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 58)));
            searchHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 68)));
            searchHeader.Controls.Add(new Label
            {
                Text = "Search",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.P.SubText
            }, 0, 0);
            searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = "Filter by name or URL",
                AccessibleName = "Search downloads",
                AccessibleDescription = "Search downloads by name or URL. Press Ctrl+F to focus this field."
            };
            keyboardShortcuts.SetToolTip(searchBox, "Search (Ctrl+F)");
            searchBox.TextChanged += (s, e) => RebuildList();
            searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    lv.Focus();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            searchHeader.Controls.Add(searchBox, 1, 0);
            var clearSearch = new Button { Text = "Clear", Dock = DockStyle.Fill, Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0) };
            clearSearch.Click += (s, e) => { searchBox.Clear(); searchBox.Focus(); };
            searchHeader.Controls.Add(clearSearch, 2, 0);
            listLayout.Controls.Add(searchHeader, 0, 0);

            listArea = new Panel { Dock = DockStyle.Fill, BackColor = Theme.P.Window };
            listArea.Controls.Add(lv);
            emptyState = new Panel { Dock = DockStyle.Fill, BackColor = Theme.P.Window, Visible = false };
            var emptyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(Theme.Scale(this, 24))
            };
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(this, 48)));
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(this, 32)));
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(this, 42)));
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(this, 42)));
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            emptyIcon = new Label
            {
                Dock = DockStyle.Fill,
                Text = Icons.Available ? "" : "↓",
                Image = Icons.Make("\uE896", Theme.P.Accent, Theme.Scale(this, 36)),
                ImageAlign = ContentAlignment.MiddleCenter,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.P.AccentText,
                BackColor = Theme.P.Window
            };
            emptyTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No downloads yet",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.CreateFont(Theme.FontHeading, FontStyle.Bold),
                ForeColor = Theme.P.Text,
                BackColor = Theme.P.Window
            };
            emptyDescription = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Paste a link or drag one here",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.P.SubText,
                BackColor = Theme.P.Window
            };
            emptyAdd = new Button { Text = "Add URL", Size = new Size(Theme.Scale(this, 112), Theme.Scale(this, 34)), Anchor = AnchorStyles.None };
            emptyAdd.Click += (s, e) => ShowAddDialog("");
            emptyLayout.Controls.Add(emptyIcon, 0, 1);
            emptyLayout.Controls.Add(emptyTitle, 0, 2);
            emptyLayout.Controls.Add(emptyDescription, 0, 3);
            emptyLayout.Controls.Add(emptyAdd, 0, 4);
            emptyState.Controls.Add(emptyLayout);
            listArea.Controls.Add(emptyState);

            dragOverlay = new Panel { Dock = DockStyle.Fill, BackColor = Theme.P.Selection, Visible = false };
            dragOverlay.AllowDrop = true;
            dragOverlay.DragEnter += OnDragEnter;
            dragOverlay.DragDrop += OnDragDrop;
            dragOverlay.DragLeave += OnDragLeave;
            dragOverlay.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "Drop link to download",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.CreateFont(Theme.FontTitle, FontStyle.Bold),
                ForeColor = Theme.P.Text,
                BackColor = Theme.P.Selection,
                AllowDrop = true
            });
            dragOverlay.Controls[0].DragEnter += OnDragEnter;
            dragOverlay.Controls[0].DragDrop += OnDragDrop;
            dragOverlay.Controls[0].DragLeave += OnDragLeave;
            listArea.Controls.Add(dragOverlay);
            listLayout.Controls.Add(listArea, 0, 1);
            listHost.Controls.Add(listLayout);

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
                Width = Theme.Scale(this, 200),
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Theme.Scale(this, 40),
                AccessibleName = "Download filters",
                AccessibleDescription = "Use the arrow keys to switch between download filters."
            };

            sidebar.Items.AddRange(FilterNames);
            sidebar.SelectedIndex = 0;

            UpdateSideCounts();

            sidebar.Width =
                settings.SidebarOpen
                    ? Theme.Scale(this, 200)
                    : 0;

            sidebar.Visible =
                settings.SidebarOpen;

            navTarget =
                settings.SidebarOpen
                    ? Theme.Scale(this, 200)
                    : 0;

            navTimer = new System.Windows.Forms.Timer
            {
                Interval = 15
            };

            navTimer.Tick += (s, e) => StepSidebar();

            sidebar.DrawItem += DrawSidebar;
            sidebar.MouseMove += (s, e) =>
            {
                int index = sidebar.IndexFromPoint(e.Location);
                if (index == hoveredSidebarIndex) return;
                hoveredSidebarIndex = index;
                sidebar.Invalidate();
            };
            sidebar.MouseLeave += (s, e) =>
            {
                if (hoveredSidebarIndex == -1) return;
                hoveredSidebarIndex = -1;
                sidebar.Invalidate();
            };

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

            lblActive.IsLink = true;
            lblActive.LinkBehavior = LinkBehavior.HoverUnderline;
            lblActive.ToolTipText = "Show all downloads";
            lblActive.Click += (s, e) => { sidebar.SelectedIndex = 0; searchBox.Clear(); lv.Focus(); };
            lblSched.IsLink = true;
            lblSched.LinkBehavior = LinkBehavior.HoverUnderline;
            lblSched.ToolTipText = "Open scheduler settings";
            lblSched.Click += (s, e) => ShowSchedule();
            lblLimit.IsLink = true;
            lblLimit.LinkBehavior = LinkBehavior.HoverUnderline;
            lblLimit.ToolTipText = "Open traffic limit settings";
            lblLimit.Click += (s, e) => ShowSettings("Traffic Limits");
            lblSpeed.IsLink = true;
            lblSpeed.LinkBehavior = LinkBehavior.HoverUnderline;
            lblSpeed.ToolTipText = "Open traffic limit settings";
            lblSpeed.Click += (s, e) => ShowSettings("Traffic Limits");

            // Toolbar
            tools = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(8, 6, 8, 6),
                Font = Theme.CreateFont(Theme.FontSection),
                ImageScalingSize = new Size(Theme.Scale(this, 20), Theme.Scale(this, 20))
            };

            // সব toolbar action একইভাবে Icon + Text দেখাবে
            tools.Items.Add(
                Btn(
                    "Sidebar",
                    "☰  ",
                    "\uE700",
                    false,
                    (s, e) => ToggleSidebar(),
                    false));
            tools.Items[tools.Items.Count - 1].ToolTipText = "Show/Hide sidebar";
            tools.Items[tools.Items.Count - 1].DisplayStyle = Icons.Available
                ? ToolStripItemDisplayStyle.Image
                : ToolStripItemDisplayStyle.Text;

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

            tools.Items.Add(new ToolStripSeparator());

            tools.Items.Add(
                Btn(
                    "Resume All",
                    "▶  ",
                    "\uE768",
                    false,
                    (s, e) => ResumeAll(),
                    false));

            tools.Items.Add(
                Btn(
                    "Pause All",
                    "❚❚  ",
                    "\uE769",
                    false,
                    (s, e) => PauseAll(),
                    false));

            tools.Items.Add(new ToolStripSeparator());

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

            BuildSettingsMenu();
            tools.Items.Add(settingsMenu);

            // ক্রম গুরুত্বপূর্ণ: Fill আগে, তারপর বাকিগুলো
            Controls.Add(listHost);
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
                ToolTipText = text switch
                {
                    "Add URL" => "Add URL (Ctrl+N)",
                    "Resume" or "Pause" => "Pause/Resume (Space)",
                    "Remove" => "Remove (Del)",
                    _ => text
                },

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

        void BuildSettingsMenu()
        {
            settingsMenu = new ToolStripDropDownButton(Icons.Available ? "Settings" : "⚙  Settings")
            {
                DisplayStyle = Icons.Available ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ShowDropDownArrow = true,
                ToolTipText = "Settings and updates",
                Padding = new Padding(4),
                Tag = new IconTag { Glyph = "\uE713", Accent = false }
            };
            settingsMenu.DropDownItems.Add("Preferences", null, (s, e) => ShowSettings());
            settingsMenu.DropDownItems.Add(new ToolStripSeparator());
            settingsMenu.DropDownItems.Add("Check for updates", null, async (s, e) =>
            {
                try
                {
                    await UpdateChecker.CheckAndShowAsync(this, false);
                }
                catch (OperationCanceledException)
                {
                    AppLog.Write("MainForm settings updates: cancelled");
                }
                catch (Exception exGuard)
                {
                    Program.ReportError("MainForm settings updates", exGuard);
                }
            });
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

        // Frequent actions retain text; secondary actions can collapse to icons before overflow.
        List<ToolStripItem> ToolbarCollapseOrder()
        {
            ToolStripItem? ByTip(string tip) =>
                tools.Items
                     .Cast<ToolStripItem>()
                     .FirstOrDefault(i =>
                         string.Equals(i.ToolTipText, tip, StringComparison.Ordinal) ||
                         string.Equals((i.Text ?? string.Empty).Trim(), tip, StringComparison.Ordinal) ||
                         (!string.IsNullOrEmpty(i.ToolTipText) && i.ToolTipText.StartsWith(tip + " (", StringComparison.Ordinal)));

            var wanted = new List<ToolStripItem?>
            {
                // Keep the file/folder actions visible as long as possible; if the window is
                // narrower than their icon widths, ToolStrip's keyboard-accessible overflow
                // remains the final fallback.
                ByTip("Schedule"), doneMenu, ByTip("Remove"), ByTip("Resume"),
                ByTip("Pause"), ByTip("Resume All"), ByTip("Pause All"), modeMenu,
                ByTip("Details"), ByTip("Settings and updates"),
                ByTip("Open File"), ByTip("Open Folder")
            };

            wanted.Insert(0, ByTip("Show/Hide sidebar"));

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
                if (i.ToolTipText == "Show/Hide sidebar")
                {
                    i.DisplayStyle = Icons.Available
                        ? ToolStripItemDisplayStyle.Image
                        : ToolStripItemDisplayStyle.Text;
                    continue;
                }

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
                        Theme.Scale(this, 20));

                    old?.Dispose();
                }
            }
        }

        void RefreshContextMenuIcons()
        {
            if (lv?.ContextMenuStrip == null) return;
            foreach (ToolStripItem item in lv.ContextMenuStrip.Items)
            {
                if (item.Tag is not ContextCommandTag tag) continue;
                var old = item.Image;
                item.Image = Icons.Make(tag.Glyph, Theme.P.Text, Theme.Scale(this, 16));
                old?.Dispose();
            }
        }

        ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip();

            ToolStripMenuItem AddItem(string text, string command, string glyph, EventHandler click)
            {
                var item = new ToolStripMenuItem(text)
                {
                    Tag = new ContextCommandTag { Command = command, Glyph = glyph },
                    Image = Icons.Make(glyph, Theme.P.Text, Theme.Scale(this, 16))
                };
                item.Click += click;
                m.Items.Add(item);
                return item;
            }

            var resume = AddItem("Resume", "resume", "\uE768", (s, e) => ResumeSelected());
            resume.ShortcutKeyDisplayString = "Space";
            var pause = AddItem("Pause", "pause", "\uE769", (s, e) => PauseSelected());
            pause.ShortcutKeyDisplayString = "Space";
            AddItem("Start now (ignore schedule)", "start-now", "\uE73A", (s, e) => StartNowSelected());

            m.Items.Add(
                new ToolStripSeparator());

            AddItem("Open File", "open-file", "\uE8E5", (s, e) => OpenSelected(false));
            AddItem("Open Folder", "open-folder", "\uE838", (s, e) => OpenSelected(true));
            AddItem("Copy file name", "copy-name", "\uE8C8", (s, e) =>
            {
                var sel = Sel();
                if (sel.Count > 0)
                    Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(i => DisplayFileName(i.FileName))));
            });
            AddItem("Copy URL", "copy-url", "\uE8C8", (s, e) =>
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

            AddItem("Properties", "properties", "\uE946", (s, e) => ShowSelectedProperties());

            m.Items.Add(
                new ToolStripSeparator());

            var remove = AddItem("Remove", "remove", "\uE74D", (s, e) => RemoveSelected());
            remove.ShortcutKeys = Keys.Delete;
            remove.ShortcutKeyDisplayString = "Del";

            m.Items.Add(new ToolStripSeparator());
            var selectAll = AddItem("Select all", "select-all", "\uE8B3", (s, e) => SelectAllRows());
            selectAll.ShortcutKeys = Keys.Control | Keys.A;

            // only in the Deleted list
            AddItem("Download again", "download-again", "\uE896", (s, e) => DownloadAgain());
            AddItem("Remove from history", "remove-history", "\uE74D", (s, e) => RemoveSelected());
            AddItem("Clear history", "clear-history", "\uE74D", (s, e) => ClearHistory());

            m.Opening += (s, e) =>
            {
                bool inDeleted = filter == DeletedFilter;

                foreach (ToolStripItem i in m.Items)
                {
                    if (i.Tag is not ContextCommandTag tag) continue;
                    bool deletedOnly = tag.Command is "download-again" or "remove-history" or "clear-history";
                    bool liveOnly = tag.Command is "resume" or "pause" or "start-now" or "open-file" or "open-folder" or "remove";

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
            bool hovered = e.Index == hoveredSidebarIndex && !sel;

            using (var b = new SolidBrush(
                sel ? p.SideSel : hovered ? p.Hover : p.SideBg))
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
                        Theme.Scale(this, 4),
                        e.Bounds.Height);
                }
            }

            int iconSize = Theme.Scale(this, 18);
            var iconRect = new Rectangle(e.Bounds.X + Theme.Scale(this, 14),
                e.Bounds.Y + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize);
            using (var icon = Icons.Make(FilterGlyphs[e.Index], sel ? Theme.P.Accent : p.SideCount, iconSize))
            {
                if (icon != null) g.DrawImage(icon, iconRect);
            }

            var tr = new Rectangle(
                e.Bounds.X + Theme.Scale(this, 42),
                e.Bounds.Y,
                SidebarWidth - Theme.Scale(this, 96),
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
                e.Bounds.X + SidebarWidth - Theme.Scale(this, 55),
                e.Bounds.Y,
                Theme.Scale(this, 45),
                e.Bounds.Height);

            TextRenderer.DrawText(
                g,
                count.ToString(),
                Font,
                cr,
                p.SideCount,
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.Right);

            if ((e.State & DrawItemState.Focus) != 0)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(e.Bounds, -Theme.Scale(this, 6), -Theme.Scale(this, 3)),
                    p.SideText, sel ? p.SideSel : p.SideBg);
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

            bool sorted = e.ColumnIndex == sortColumn;
            var tr = new Rectangle(
                e.Bounds.X + Theme.Scale(this, 8),
                e.Bounds.Y,
                Math.Max(0, e.Bounds.Width - Theme.Scale(this, sorted ? 28 : 12)),
                e.Bounds.Height);
            string headerText = (e.Header?.Text ?? "") +
                (sorted ? (sortAscending ? "  \u25B2" : "  \u25BC") : "");

            TextRenderer.DrawText(
                e.Graphics,
                headerText,
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
                var labelRect = new Rectangle(
                    r.Right - 48,
                    r.Y,
                    44,
                    r.Height);
                var bar = new Rectangle(
                    r.X + 6,
                    r.Y + (r.Height - 18) / 2,
                    Math.Max(0, labelRect.Left - (r.X + 10)),
                    18);

                g.SmoothingMode =
                    SmoothingMode.AntiAlias;

                int trackRadius = Math.Min(8, Math.Min(bar.Width / 2, bar.Height / 2));
                using (var b = new SolidBrush(p.Track))
                {
                    if (trackRadius == 0) g.FillRectangle(b, bar);
                    else
                    {
                        using var gp = RoundRect(bar, trackRadius);
                        g.FillPath(b, gp);
                    }
                }

                string label;

                if (it.TotalBytes > 0)
                {
                    int w = Math.Clamp((int)Math.Round(bar.Width * it.Percent / 100.0), 0, bar.Width);

                    if (w > 0)
                    {
                        var fill = new Rectangle(
                            bar.X,
                            bar.Y,
                            w,
                            bar.Height);

                        int fillRadius = Math.Min(8, Math.Min(fill.Width / 2, fill.Height / 2));
                        using (var b = new SolidBrush(RowColor(it)))
                        {
                            if (fillRadius == 0)
                                g.FillRectangle(b, fill);
                            else
                            {
                                using var gp = RoundRect(fill, fillRadius);
                                g.FillPath(b, gp);
                            }
                        }
                    }
                }

                if (it.State == DlState.Error)
                    label = "Error";
                else if (it.State == DlState.Paused)
                    label = "Paused";
                else if (it.TotalBytes > 0)
                    label = it.Percent.ToString("0") + "%";
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
                    labelRect,
                    p.Text,
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.Right |
                    TextFormatFlags.EndEllipsis);
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

        bool IsTextInputFocused() => ContainsTextInputFocus(this);

        static bool ContainsTextInputFocus(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control.Focused && (control is TextBoxBase or ComboBox or NumericUpDown))
                    return true;
                if (control.HasChildren && ContainsTextInputFocus(control))
                    return true;
            }
            return false;
        }

        void OnMainKeyDown(object? sender, KeyEventArgs e)
        {
            bool textInputFocused = IsTextInputFocused();

            if (e.Control && e.KeyCode == Keys.F)
            {
                searchBox.Focus();
                searchBox.SelectAll();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (!textInputFocused && e.Control && e.KeyCode == Keys.N)
            {
                string clipboardText = "";
                try
                {
                    if (Clipboard.ContainsText()) clipboardText = Clipboard.GetText().Trim();
                }
                catch (Exception exIgnored) { AppLog.Write("MainForm Ctrl+N clipboard (ignored): " + exIgnored.Message); }
                ShowAddDialog(clipboardText);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (!textInputFocused && lv.Focused && e.Control && e.KeyCode == Keys.V)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        ShowAddDialog(Clipboard.GetText().Trim());
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                }
                catch (Exception exIgnored) { AppLog.Write("MainForm Ctrl+V clipboard (ignored): " + exIgnored.Message); }
                return;
            }

            if (!textInputFocused && lv.Focused && e.KeyCode == Keys.Space)
            {
                var selected = Sel();
                if (selected.Count > 0)
                {
                    if (selected.Any(it => it.State is DlState.Downloading or DlState.Queued))
                        PauseSelected();
                    else
                        ResumeSelected();
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            int index = e.KeyCode switch
            {
                Keys.D1 or Keys.NumPad1 => 0,
                Keys.D2 or Keys.NumPad2 => 1,
                Keys.D3 or Keys.NumPad3 => 2,
                Keys.D4 or Keys.NumPad4 => 3,
                Keys.D5 or Keys.NumPad5 => 4,
                Keys.D6 or Keys.NumPad6 => 5,
                _ => -1
            };
            if (e.Control && index >= 0 && index < sidebar.Items.Count)
            {
                sidebar.SelectedIndex = index;
                lv.Focus();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape && searchBox.Focused && searchBox.TextLength > 0)
            {
                searchBox.Text = string.Empty;
                lv.Focus();
                e.Handled = true;
                e.SuppressKeyPress = true;
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
                    ? Theme.Scale(this, 200)
                    : 0;
            navStartWidth = sidebar.Width;
            navAnimation.Restart();

            if (settings.SidebarOpen)
                sidebar.Visible = true;

            navTimer.Start();

            SaveState();
        }

        void StepSidebar()
        {
            double progress = Math.Clamp(navAnimation.Elapsed.TotalMilliseconds / 150.0, 0, 1);
            double eased = 1 - Math.Pow(1 - progress, 3);
            int w = progress >= 1
                ? navTarget
                : navStartWidth + (int)Math.Round((navTarget - navStartWidth) * eased);

            sidebar.Width = w;

            FitLastColumn();

            if (w == navTarget)
            {
                navTimer.Stop();
                navAnimation.Stop();

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

        void ShowSelectedProperties()
        {
            var selected = Sel().FirstOrDefault();
            if (selected == null) return;
            details.SetItem(selected);
            if (!details.Visible)
                ToggleDetails();
            else
                details.Invalidate();
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
                        ? Theme.Scale(this, 26)
                        : Theme.Scale(this, 34));

            lv.Invalidate();
        }

        void RefreshDpiMetrics()
        {
            if (sidebar != null)
            {
                sidebar.ItemHeight = Theme.Scale(this, 40);
                sidebar.Width = settings.SidebarOpen ? Theme.Scale(this, 200) : 0;
                navTarget = settings.SidebarOpen ? Theme.Scale(this, 200) : 0;
            }
            if (details != null) details.Height = Theme.Scale(details, 212);
            if (tools != null)
                tools.ImageScalingSize = new Size(Theme.Scale(this, 20), Theme.Scale(this, 20));
            ApplyCompactView();
            RefreshToolIcons();
            FitLastColumn();
        }

        // নামের কলাম বাদে শেষ কলাম বাকি জায়গা ভরবে
        void FitLastColumn()
        {
            if (lv == null ||
                lv.Columns.Count == 0)
                return;

            var widths = DownloadColumnLayout.Fit(Math.Max(0, lv.ClientSize.Width - 4));
            for (int i = 0; i < Math.Min(widths.Length, lv.Columns.Count); i++)
                if (lv.Columns[i].Width != widths[i])
                    lv.Columns[i].Width = widths[i];
        }
    }
}
