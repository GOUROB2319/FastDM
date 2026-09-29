#nullable disable
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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FastDM
{
    // ====================== ডেটা মডেল ======================
    public enum DlState { Queued, Downloading, Paused, Completed, Error }

    public class Segment
    {
        public long Start { get; set; }
        public long End { get; set; }          // inclusive, -1 = অজানা
        public long Downloaded { get; set; }
        public bool Finished { get; set; }
    }

    public class AppSettings
    {
        public string DefaultFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        public int Connections { get; set; } = 8;       // প্রতি ফাইলে কয়টা কানেকশন
        public int MaxSimultaneous { get; set; } = 3;   // একসাথে কয়টা ফাইল
    }

    public class DownloadItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Url { get; set; }
        public string FileName { get; set; }
        public string Folder { get; set; }
        public long TotalBytes { get; set; }
        public bool SupportsRange { get; set; }
        public DlState State { get; set; }
        public string Error { get; set; }
        public DateTime Added { get; set; } = DateTime.Now;
        public List<Segment> Segments { get; set; } = new List<Segment>();

        long _downloaded;
        public long Downloaded
        {
            get => Interlocked.Read(ref _downloaded);
            set => Interlocked.Exchange(ref _downloaded, value);
        }
        public void AddDownloaded(long n) => Interlocked.Add(ref _downloaded, n);

        [JsonIgnore] public string SavePath => Path.Combine(Folder, FileName);
        [JsonIgnore] public string TempPath => SavePath + ".part";
        [JsonIgnore] public CancellationTokenSource Cts { get; set; }
        [JsonIgnore] public bool RemoveRequested { get; set; }
        [JsonIgnore] public bool DeleteFile { get; set; }
        [JsonIgnore] public double Speed { get; set; }
        [JsonIgnore] public long LastBytes { get; set; }
        [JsonIgnore] public DateTime LastTick { get; set; }
        [JsonIgnore]
        public double Percent =>
            TotalBytes > 0 ? Math.Min(100.0, Downloaded * 100.0 / TotalBytes) : 0;
    }

    public class AppData
    {
        public AppSettings Settings { get; set; } = new AppSettings();
        public List<DownloadItem> Items { get; set; } = new List<DownloadItem>();
    }

    // ====================== ডাউনলোড ইঞ্জিন ======================
    public static class Engine
    {
        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.None
            };
            var c = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) FastDM/1.0");
            return c;
        }

        public static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        public static string NameFromUrl(string url)
        {
            try
            {
                string n = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath));
                if (!string.IsNullOrWhiteSpace(n)) return Sanitize(n);
            }
            catch { }
            return "download.bin";
        }

        // ফাইলের সাইজ, resume সাপোর্ট আর নাম জানার জন্য
        public static async Task ProbeAsync(DownloadItem it, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, it.Url);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            if (resp.StatusCode == HttpStatusCode.PartialContent && resp.Content.Headers.ContentRange?.Length != null)
            {
                it.TotalBytes = resp.Content.Headers.ContentRange.Length.Value;
                it.SupportsRange = true;
            }
            else
            {
                it.TotalBytes = resp.Content.Headers.ContentLength ?? 0;
                it.SupportsRange = false;
            }

            if (string.IsNullOrWhiteSpace(it.FileName))
            {
                var cd = resp.Content.Headers.ContentDisposition;
                string name = cd?.FileNameStar ?? cd?.FileName;
                if (!string.IsNullOrWhiteSpace(name)) it.FileName = Sanitize(name.Trim('"'));
                else
                {
                    var uri = resp.RequestMessage?.RequestUri;
                    it.FileName = NameFromUrl(uri != null ? uri.ToString() : it.Url);
                }
            }
        }

        public static async Task RunAsync(DownloadItem it, int connections, CancellationToken ct)
        {
            Directory.CreateDirectory(it.Folder);

            bool fresh = it.Segments.Count == 0 || !it.SupportsRange
                         || !File.Exists(it.TempPath)
                         || new FileInfo(it.TempPath).Length != it.TotalBytes;
            if (fresh)
            {
                it.Segments = new List<Segment>();
                it.Downloaded = 0;
                if (it.SupportsRange && it.TotalBytes > 0)
                {
                    int n = (int)Math.Max(1, Math.Min(connections, it.TotalBytes / (1 << 20)));
                    long size = it.TotalBytes / n;
                    for (int i = 0; i < n; i++)
                    {
                        long start = i * size;
                        long end = (i == n - 1) ? it.TotalBytes - 1 : start + size - 1;
                        it.Segments.Add(new Segment { Start = start, End = end });
                    }
                }
                else
                {
                    it.Segments.Add(new Segment { Start = 0, End = it.TotalBytes > 0 ? it.TotalBytes - 1 : -1 });
                }
                using (var fs = new FileStream(it.TempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    if (it.SupportsRange) fs.SetLength(it.TotalBytes);
                }
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tasks = it.Segments.Where(s => !s.Finished).Select(s => Task.Run(async () =>
            {
                try { await SegmentAsync(it, s, linked.Token).ConfigureAwait(false); }
                catch when (!ct.IsCancellationRequested) { linked.Cancel(); throw; }
            })).ToList();

            try { await Task.WhenAll(tasks); }
            catch
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                var faulted = tasks.FirstOrDefault(t => t.IsFaulted);
                throw faulted?.Exception?.GetBaseException() ?? new IOException("Download failed.");
            }

            if (it.SupportsRange && it.Segments.Any(s => !s.Finished))
                throw new IOException("Download incomplete.");

            if (File.Exists(it.SavePath)) File.Delete(it.SavePath);
            File.Move(it.TempPath, it.SavePath);
        }

        static async Task SegmentAsync(DownloadItem it, Segment seg, CancellationToken ct)
        {
            int attempt = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!it.SupportsRange)
                    {
                        it.AddDownloaded(-seg.Downloaded);
                        seg.Downloaded = 0;
                    }
                    long pos = seg.Start + seg.Downloaded;

                    using var req = new HttpRequestMessage(HttpMethod.Get, it.Url);
                    if (it.SupportsRange) req.Headers.Range = new RangeHeaderValue(pos, seg.End);

                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    if (it.SupportsRange && resp.StatusCode != HttpStatusCode.PartialContent)
                        throw new IOException("Server does not support resume.");

                    using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var fs = new FileStream(it.TempPath, FileMode.Open, FileAccess.Write,
                                                  FileShare.ReadWrite, 1 << 16, true);
                    fs.Seek(pos, SeekOrigin.Begin);

                    var buf = new byte[1 << 16];
                    int n;
                    while ((n = await input.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
                    {
                        if (it.SupportsRange)
                        {
                            long room = seg.End - (seg.Start + seg.Downloaded) + 1;
                            if (n > room) n = (int)room;
                        }
                        await fs.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                        seg.Downloaded += n;
                        it.AddDownloaded(n);
                        if (it.SupportsRange && seg.Start + seg.Downloaded > seg.End) break;
                    }

                    if (!it.SupportsRange) fs.SetLength(fs.Position);
                    if (it.SupportsRange && seg.Start + seg.Downloaded <= seg.End)
                        throw new IOException("Connection closed early.");

                    seg.Finished = true;
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) when (++attempt < 6)
                {
                    await Task.Delay(1500 * attempt, ct).ConfigureAwait(false);   // আবার চেষ্টা
                }
            }
        }
    }

    // ====================== ফ্লিকার-ফ্রি ListView ======================
    class BufferedListView : ListView
    {
        public BufferedListView() { DoubleBuffered = true; }
    }

    // ====================== Add URL ডায়ালগ ======================
    class AddUrlForm : Form
    {
        readonly TextBox txtUrls, txtName, txtFolder;
        public string[] Urls => txtUrls.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        public string FileNameText => txtName.Text.Trim();
        public string Folder => txtFolder.Text.Trim();

        public AddUrlForm(string initialUrl, string folder)
        {
            Text = "Add Download";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(560, 336);
            Font = new Font("Segoe UI", 9.5f);

            Controls.Add(new Label { Text = "URL(s) — one link per line", Location = new Point(16, 12), AutoSize = true });
            txtUrls = new TextBox { Location = new Point(16, 34), Size = new Size(528, 110), Multiline = true, ScrollBars = ScrollBars.Vertical, Text = initialUrl ?? "" };
            Controls.Add(txtUrls);

            Controls.Add(new Label { Text = "File name (optional, single link only)", Location = new Point(16, 156), AutoSize = true });
            txtName = new TextBox { Location = new Point(16, 178), Size = new Size(528, 26) };
            Controls.Add(txtName);

            Controls.Add(new Label { Text = "Save to", Location = new Point(16, 214), AutoSize = true }); txtFolder = new TextBox { Location = new Point(16, 236), Size = new Size(436, 26), Text = folder };
            Controls.Add(txtFolder);
            var browse = new Button { Text = "Browse…", Location = new Point(460, 235), Size = new Size(84, 28) };
            browse.Click += (s, e) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
            };
            Controls.Add(browse);

            var start = new Button { Text = "Start Download", Location = new Point(196, 284), Size = new Size(124, 34) };
            var later = new Button { Text = "Download Later", Location = new Point(326, 284), Size = new Size(120, 34) };
            var cancel = new Button { Text = "Cancel", Location = new Point(452, 284), Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
            start.Click += (s, e) => { if (Check()) DialogResult = DialogResult.OK; };
            later.Click += (s, e) => { if (Check()) DialogResult = DialogResult.Yes; };
            Controls.AddRange(new Control[] { start, later, cancel });
            AcceptButton = start; CancelButton = cancel;
        }

        bool Check()
        {
            if (Urls.Length == 0) { MessageBox.Show(this, "Please enter at least one URL."); return false; }
            foreach (var u in Urls)
            {
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                { MessageBox.Show(this, "Invalid link (only http/https):\n" + u); return false; }
            }
            if (Folder.Length == 0) { MessageBox.Show(this, "Please choose a folder."); return false; }
            return true;
        }
    }

    // ====================== Settings ডায়ালগ ======================
    class SettingsForm : Form
    {
        readonly NumericUpDown numConn, numSim;
        readonly TextBox txtFolder;
        readonly AppSettings s;

        public SettingsForm(AppSettings settings)
        {
            s = settings;
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(440, 230);
            Font = new Font("Segoe UI", 9.5f);

            Controls.Add(new Label { Text = "Connections per download (1–16)", Location = new Point(16, 20), AutoSize = true });
            numConn = new NumericUpDown { Location = new Point(300, 17), Width = 120, Minimum = 1, Maximum = 16, Value = Math.Clamp(s.Connections, 1, 16) };
            Controls.Add(numConn);

            Controls.Add(new Label { Text = "Simultaneous downloads (1–10)", Location = new Point(16, 60), AutoSize = true });
            numSim = new NumericUpDown { Location = new Point(300, 57), Width = 120, Minimum = 1, Maximum = 10, Value = Math.Clamp(s.MaxSimultaneous, 1, 10) };
            Controls.Add(numSim);

            Controls.Add(new Label { Text = "Default save folder", Location = new Point(16, 100), AutoSize = true });
            txtFolder = new TextBox { Location = new Point(16, 124), Width = 320, Text = s.DefaultFolder };
            Controls.Add(txtFolder);
            var browse = new Button { Text = "Browse…", Location = new Point(344, 122), Size = new Size(80, 28) };
            browse.Click += (a, b) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
            };
            Controls.Add(browse);

            var ok = new Button { Text = "Save", Location = new Point(230, 178), Size = new Size(90, 34) };
            var cancel = new Button { Text = "Cancel", Location = new Point(330, 178), Size = new Size(90, 34), DialogResult = DialogResult.Cancel };
            ok.Click += (a, b) =>
            {
                s.Connections = (int)numConn.Value;
                s.MaxSimultaneous = (int)numSim.Value;
                if (txtFolder.Text.Trim().Length > 0) s.DefaultFolder = txtFolder.Text.Trim();
                DialogResult = DialogResult.OK;
            };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    // ====================== মূল ফর্ম ======================
    public partial class Form1 : Form
    {
        // থিম
        static readonly Color Accent = ColorTranslator.FromHtml("#5B6CFF");
        static readonly Color SideBg = ColorTranslator.FromHtml("#1E2233");
        static readonly Color SideSel = ColorTranslator.FromHtml("#2F3550");
        static readonly Color TextDark = ColorTranslator.FromHtml("#2D3142");
        static readonly Color Green = ColorTranslator.FromHtml("#2ECC71");
        static readonly Color Orange = ColorTranslator.FromHtml("#F5A623");
        static readonly Color Red = ColorTranslator.FromHtml("#FF5C77");
        static readonly Color Gray = ColorTranslator.FromHtml("#9AA0B4");

        static readonly string[] FilterNames = { "All Downloads", "Downloading", "Queued / Paused", "Completed", "Errors" };
        static readonly string[] FileExts =
        {
            ".zip", ".rar", ".7z", ".iso", ".exe", ".msi", ".apk", ".pdf",
            ".mp4", ".mkv", ".avi", ".mp3", ".flac", ".bin", ".dmg", ".torrent"
        };

        readonly List<DownloadItem> items = new List<DownloadItem>();
        AppSettings settings = new AppSettings();
        readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastDM");
        string DataFile => Path.Combine(dataDir, "state.json");

        BufferedListView lv;
        ListBox sidebar;
        ToolStripStatusLabel lblActive, lblSpeed;
        System.Windows.Forms.Timer timer;
        int filter = 0;
        bool dirty;
        string lastClip;
        DateTime lastSave = DateTime.UtcNow;

        public Form1()
        {
            InitializeComponent();
            LoadState();
            BuildUI();
            RebuildList();
            try { lastClip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null; } catch { }

            timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += OnTick;
            timer.Start();

            Activated += (s, e) => CheckClipboard();
            FormClosing += (s, e) =>
            {
                foreach (var it in items) it.Cts?.Cancel();
                SaveState();
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
            BackColor = Color.White;
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
                SmallImageList = new ImageList { ImageSize = new Size(1, 34) }   // row height
            };
            lv.Columns.Add("Name", 300);
            lv.Columns.Add("Size", 90);
            lv.Columns.Add("Progress", 170);
            lv.Columns.Add("Status", 100);
            lv.Columns.Add("Speed", 95);
            lv.Columns.Add("ETA", 80);
            lv.Columns.Add("Added", 120);
            lv.DrawColumnHeader += (s, e) => e.DrawDefault = true;
            lv.DrawItem += (s, e) => { };
            lv.DrawSubItem += DrawSub;
            lv.DoubleClick += (s, e) => OnDoubleClick();
            lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            lv.DragEnter += OnDragEnter;
            lv.DragDrop += OnDragDrop;
            lv.ContextMenuStrip = BuildContextMenu();

            // Sidebar
            sidebar = new ListBox
            {
                Dock = DockStyle.Left,
                Width = 200,
                BackColor = SideBg,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 40
            };
            sidebar.Items.AddRange(FilterNames);
            sidebar.SelectedIndex = 0;
            sidebar.DrawItem += DrawSidebar;
            sidebar.SelectedIndexChanged += (s, e) =>
            {
                filter = Math.Max(0, sidebar.SelectedIndex);
                RebuildList();
            };

            // Status bar
            var status = new StatusStrip { SizingGrip = false };
            lblActive = new ToolStripStatusLabel("Active: 0") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            lblSpeed = new ToolStripStatusLabel("↓ 0 B/s");
            status.Items.Add(lblActive);
            status.Items.Add(lblSpeed);

            // Toolbar
            var tools = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(8, 6, 8, 6),
                Font = new Font("Segoe UI", 10f),
                BackColor = Color.White,
                RenderMode = ToolStripRenderMode.System
            };
            tools.Items.Add(Btn("＋ Add URL", (s, e) => ShowAddDialog("")));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Btn("▶ Resume", (s, e) => ResumeSelected()));
            tools.Items.Add(Btn("❚❚ Pause", (s, e) => PauseSelected()));
            tools.Items.Add(Btn("Pause All", (s, e) => PauseAll()));
            tools.Items.Add(Btn("✕ Remove", (s, e) => RemoveSelected()));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Btn("Open File", (s, e) => OpenSelected(false)));
            tools.Items.Add(Btn("Open Folder", (s, e) => OpenSelected(true)));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Btn("⚙ Settings", (s, e) => ShowSettings()));

            // ক্রম গুরুত্বপূর্ণ: Fill আগে, তারপর বাকিগুলো
            Controls.Add(lv);
            Controls.Add(sidebar);
            Controls.Add(status);
            Controls.Add(tools);
        }

        static ToolStripButton Btn(string text, EventHandler h)
        {
            var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(4) };
            b.Click += h;
            return b;
        }

        ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Resume", null, (s, e) => ResumeSelected());
            m.Items.Add("Pause", null, (s, e) => PauseSelected());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Open File", null, (s, e) => OpenSelected(false));
            m.Items.Add("Open Folder", null, (s, e) => OpenSelected(true));
            m.Items.Add("Copy URL", null, (s, e) =>
            {
                var sel = Sel();
                if (sel.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(i => i.Url)));
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Remove", null, (s, e) => RemoveSelected());
            return m;
        }

        // ---------- ড্রয়িং ----------
        void DrawSidebar(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? SideSel : SideBg)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);

            var tr = new Rectangle(e.Bounds.X + 18, e.Bounds.Y, e.Bounds.Width - 70, e.Bounds.Height);
            TextRenderer.DrawText(g, FilterNames[e.Index], Font, tr, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            int count = items.Count(i => Match(e.Index, i));
            var cr = new Rectangle(e.Bounds.Right - 55, e.Bounds.Y, 45, e.Bounds.Height);
            TextRenderer.DrawText(g, count.ToString(), Font, cr, Color.FromArgb(160, 168, 200),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        void DrawSub(object sender, DrawListViewSubItemEventArgs e)
        {
            var it = (DownloadItem)e.Item.Tag;
            var g = e.Graphics;
            var r = new Rectangle(e.Bounds.X, e.Bounds.Y, lv.Columns[e.ColumnIndex].Width, e.Bounds.Height);

            Color back = e.Item.Selected ? Color.FromArgb(222, 230, 255)
                       : (e.ItemIndex % 2 == 0 ? Color.White : Color.FromArgb(248, 249, 252));
            using (var b = new SolidBrush(back)) g.FillRectangle(b, r);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            if (e.ColumnIndex == 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(StateColor(it.State)))
                    g.FillEllipse(b, r.X + 8, r.Y + (r.Height - 10) / 2, 10, 10);
                var tr = new Rectangle(r.X + 26, r.Y, r.Width - 30, r.Height);
                TextRenderer.DrawText(g, e.SubItem.Text, Font, tr, TextDark, flags);
            }
            else if (e.ColumnIndex == 2)
            {
                var bar = new Rectangle(r.X + 6, r.Y + (r.Height - 18) / 2, r.Width - 14, 18);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = RoundRect(bar, 8))
                using (var b = new SolidBrush(Color.FromArgb(230, 233, 242)))
                    g.FillPath(b, p);

                string label;
                if (it.TotalBytes > 0)
                {
                    int w = (int)(bar.Width * it.Percent / 100.0);
                    if (w > 6)
                    {
                        var fill = new Rectangle(bar.X, bar.Y, w, bar.Height);
                        using (var p = RoundRect(fill, 8))
                        using (var b = new SolidBrush(StateColor(it.State)))
                            g.FillPath(b, p);
                    }
                    label = it.Percent.ToString("0.0") + "%";
                }
                else label = it.Downloaded > 0 ? Fmt(it.Downloaded) : "—";
                TextRenderer.DrawText(g, label, new Font("Segoe UI", 8.5f, FontStyle.Bold), bar, TextDark,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }
            else
            {
                var tr = new Rectangle(r.X + 6, r.Y, r.Width - 8, r.Height);
                TextRenderer.DrawText(g, e.SubItem.Text, Font, tr, TextDark, flags);
            }
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static Color StateColor(DlState s)
        {
            switch (s)
            {
                case DlState.Downloading: return Accent;
                case DlState.Completed: return Green;
                case DlState.Paused: return Orange;
                case DlState.Error: return Red;
                default: return Gray;
            }
        }

        // ---------- লিস্ট ----------
        static bool Match(int f, DownloadItem i)
        {
            switch (f)
            {
                case 1: return i.State == DlState.Downloading;
                case 2: return i.State == DlState.Queued || i.State == DlState.Paused;
                case 3: return i.State == DlState.Completed;
                case 4: return i.State == DlState.Error;
                default: return true;
            }
        }

        void RebuildList()
        {
            var selected = lv.SelectedItems.Cast<ListViewItem>().Select(l => ((DownloadItem)l.Tag).Id).ToHashSet();
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var it in items.Where(i => Match(filter, i)).OrderByDescending(i => i.Added))
            {
                var lvi = new ListViewItem(new[] { "", "", "", "", "", "", "" }) { Tag = it };
                lv.Items.Add(lvi);
                FillRow(lvi, it);
                if (selected.Contains(it.Id)) lvi.Selected = true;
            }
            lv.EndUpdate();
            sidebar.Invalidate();
        }

        static void SetIf(ListViewItem l, int idx, string text)
        {
            if (l.SubItems[idx].Text != text) l.SubItems[idx].Text = text;
        }

        void FillRow(ListViewItem l, DownloadItem it)
        {
            SetIf(l, 0, it.FileName);
            SetIf(l, 1, it.TotalBytes > 0 ? Fmt(it.TotalBytes) : "—");
            SetIf(l, 2, it.Percent.ToString("0.0") + "%");
            SetIf(l, 3, StateText(it.State));
            SetIf(l, 4, it.State == DlState.Downloading ? Fmt(it.Speed) + "/s" : "");
            SetIf(l, 5, Eta(it));
            SetIf(l, 6, it.Added.ToString("dd MMM HH:mm"));
            l.ToolTipText = it.State == DlState.Error ? it.Error : it.Url;
        }

        static string StateText(DlState s) =>
            s == DlState.Completed ? "Complete" : s.ToString();

        static string Fmt(double b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (i == 0 ? b.ToString("0") : b.ToString("0.##")) + " " + u[i];
        }

        static string Eta(DownloadItem it)
        {
            if (it.State != DlState.Downloading || it.Speed < 1 || it.TotalBytes <= 0) return "";
            var t = TimeSpan.FromSeconds((it.TotalBytes - it.Downloaded) / it.Speed);
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1) return t.Minutes + "m " + t.Seconds + "s";
            return t.Seconds + "s";
        }

        List<DownloadItem> Sel() =>
            lv.SelectedItems.Cast<ListViewItem>().Select(l => (DownloadItem)l.Tag).ToList();

        void MarkChanged() { dirty = true; }

        // ---------- টাইমার: কিউ + স্পিড ----------
        void OnTick(object sender, EventArgs e)
        {
            int active = items.Count(i => i.State == DlState.Downloading);
            foreach (var it in items.Where(i => i.State == DlState.Queued).OrderBy(i => i.Added).ToList())
            {
                if (active >= settings.MaxSimultaneous) break;
                StartDownload(it);
                active++;
            }

            var now = DateTime.UtcNow;
            double total = 0;
            foreach (var it in items)
            {
                if (it.State == DlState.Downloading)
                {
                    double dt = (now - it.LastTick).TotalSeconds;
                    if (dt >= 0.4)
                    {
                        long cur = it.Downloaded;
                        double inst = (cur - it.LastBytes) / dt;
                        it.Speed = it.Speed <= 0 ? inst : it.Speed * 0.6 + inst * 0.4;
                        it.LastBytes = cur;
                        it.LastTick = now;
                    }
                    total += it.Speed;
                }
                else it.Speed = 0;
            }

            if (dirty) { dirty = false; RebuildList(); }
            else
            {
                foreach (ListViewItem l in lv.Items) FillRow(l, (DownloadItem)l.Tag);
                lv.Invalidate();
            }

            lblActive.Text = "Active: " + items.Count(i => i.State == DlState.Downloading) +
                             "   Queued: " + items.Count(i => i.State == DlState.Queued);
            lblSpeed.Text = "↓ " + Fmt(total) + "/s";
            sidebar.Invalidate();

            if (active > 0 && (now - lastSave).TotalSeconds > 3) SaveState();
        }

        // ---------- ডাউনলোড কন্ট্রোল ----------
        async void StartDownload(DownloadItem it)
        {
            var cts = new CancellationTokenSource();
            it.Cts = cts;
            it.State = DlState.Downloading;
            it.Error = null;
            it.LastBytes = it.Downloaded;
            it.LastTick = DateTime.UtcNow;
            it.Speed = 0;
            MarkChanged();

            try
            {
                await Engine.RunAsync(it, settings.Connections, cts.Token);
                if (it.TotalBytes <= 0) it.TotalBytes = it.Downloaded;
                it.State = DlState.Completed;
            }
            catch (OperationCanceledException) { it.State = DlState.Paused; }
            catch (Exception ex) { it.State = DlState.Error; it.Error = ex.Message; }
            finally { it.Cts = null; cts.Dispose(); }

            it.Speed = 0;
            if (it.RemoveRequested)
            {
                TryDelete(it.TempPath);
                if (it.State == DlState.Completed && it.DeleteFile) TryDelete(it.SavePath);
            }
            MarkChanged();
            SaveState();
        }

        void ResumeSelected()
        {
            foreach (var it in Sel())
                if (it.State == DlState.Paused || it.State == DlState.Error)
                { it.State = DlState.Queued; it.Error = null; }
            MarkChanged();
        }

        void PauseSelected()
        {
            foreach (var it in Sel())
            {
                if (it.State == DlState.Downloading) it.Cts?.Cancel();
                else if (it.State == DlState.Queued) it.State = DlState.Paused;
            }
            MarkChanged();
        }

        void PauseAll()
        {
            foreach (var it in items)
            {
                if (it.State == DlState.Downloading) it.Cts?.Cancel();
                else if (it.State == DlState.Queued) it.State = DlState.Paused;
            }
            MarkChanged();
        }

        void RemoveSelected()
        {
            var sel = Sel();
            if (sel.Count == 0) return;

            bool deleteDone = false;
            if (sel.Any(i => i.State == DlState.Completed))
            {
                var r = MessageBox.Show(this,
                    "Remove " + sel.Count + " download(s) from the list?\n\n" +
                    "Yes = remove from list only\nNo = remove AND delete completed file(s) from disk",
                    "Remove", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                deleteDone = r == DialogResult.No;
            }
            else
            {
                if (MessageBox.Show(this, "Remove " + sel.Count + " download(s)? Partial data will be deleted.",
                    "Remove", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            }

            foreach (var it in sel)
            {
                it.RemoveRequested = true;
                it.DeleteFile = deleteDone;
                items.Remove(it);
                if (it.State == DlState.Downloading) it.Cts?.Cancel();   // ফাইল ডিলিট StartDownload শেষে হবে
                else
                {
                    TryDelete(it.TempPath);
                    if (it.State == DlState.Completed && deleteDone) TryDelete(it.SavePath);
                }
            }
            MarkChanged();
            SaveState();
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        void OpenSelected(bool folder)
        {
            var sel = Sel();
            if (sel.Count == 0) return;
            var it = sel[0];
            try
            {
                if (folder)
                {
                    string target = File.Exists(it.SavePath) ? it.SavePath
                                  : File.Exists(it.TempPath) ? it.TempPath : null;
                    if (target != null) Process.Start("explorer.exe", "/select,\"" + target + "\"");
                    else if (Directory.Exists(it.Folder)) Process.Start("explorer.exe", "\"" + it.Folder + "\"");
                }
                else
                {
                    if (it.State != DlState.Completed || !File.Exists(it.SavePath))
                    { MessageBox.Show(this, "File is not completed yet."); return; }
                    Process.Start(new ProcessStartInfo(it.SavePath) { UseShellExecute = true });
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        }

        void OnDoubleClick()
        {
            var sel = Sel();
            if (sel.Count == 0) return;
            var it = sel[0];
            if (it.State == DlState.Completed) OpenSelected(false);
            else if (it.State == DlState.Downloading) PauseSelected();
            else ResumeSelected();
        }

        // ---------- Add / Settings ----------
        async void ShowAddDialog(string url)
        {
            using var dlg = new AddUrlForm(url, settings.DefaultFolder);
            var result = dlg.ShowDialog(this);
            if (result != DialogResult.OK && result != DialogResult.Yes) return;

            bool startNow = result == DialogResult.OK;
            string[] urls = dlg.Urls;
            string nameText = dlg.FileNameText;
            string folder = dlg.Folder;

            lblActive.Text = "Fetching file info…";
            foreach (var u in urls)
            {
                var it = new DownloadItem
                {
                    Url = u,
                    Folder = folder,
                    FileName = urls.Length == 1 && nameText.Length > 0 ? Engine.Sanitize(nameText) : ""
                };
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    await Engine.ProbeAsync(it, cts.Token);
                }
                catch { /* ইনফো না পেলেও যোগ হবে; আসল এরর ডাউনলোডের সময় দেখাবে */ }

                if (string.IsNullOrWhiteSpace(it.FileName)) it.FileName = Engine.NameFromUrl(u);
                it.FileName = UniqueName(folder, it.FileName);
                it.State = startNow ? DlState.Queued : DlState.Paused;
                items.Add(it);
            }
            MarkChanged();
            SaveState();
        }

        string UniqueName(string folder, string name)
        {
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            string cand = name;
            int n = 1;
            while (File.Exists(Path.Combine(folder, cand)) ||
                   File.Exists(Path.Combine(folder, cand) + ".part") ||
                   items.Any(i => i.Folder == folder && i.FileName.Equals(cand, StringComparison.OrdinalIgnoreCase)))
                cand = stem + " (" + (n++) + ")" + ext;
            return cand;
        }

        void ShowSettings()
        {
            using var f = new SettingsForm(settings);
            if (f.ShowDialog(this) == DialogResult.OK) SaveState();
        }

        // ---------- ড্র্যাগ-ড্রপ + ক্লিপবোর্ড ----------
        void OnDragEnter(object s, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object s, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.Text) is string t) ShowAddDialog(t.Trim());
        }

        void CheckClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                string t = Clipboard.GetText().Trim();
                if (t == lastClip) return;
                lastClip = t;
                if (!Uri.TryCreate(t, UriKind.Absolute, out var u) ||
                    (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return;
                string ext = Path.GetExtension(u.AbsolutePath).ToLowerInvariant();
                if (!FileExts.Contains(ext)) return;
                BeginInvoke(new Action(() => ShowAddDialog(t)));
            }
            catch { }
        }

        // ---------- সেভ / লোড ----------
        void SaveState()
        {
            lastSave = DateTime.UtcNow;
            try
            {
                Directory.CreateDirectory(dataDir);
                var json = JsonSerializer.Serialize(
                    new AppData { Settings = settings, Items = items.ToList() },
                    new JsonSerializerOptions { WriteIndented = true });
                string tmp = DataFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, DataFile, true);
            }
            catch { }
        }

        void LoadState()
        {
            try
            {
                if (!File.Exists(DataFile)) return;
                var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(DataFile));
                if (data == null) return;
                if (data.Settings != null) settings = data.Settings;
                foreach (var it in data.Items ?? new List<DownloadItem>())
                {
                    if (it.State == DlState.Downloading) it.State = DlState.Paused;
                    items.Add(it);
                }
            }
            catch { }
        }
    }
}