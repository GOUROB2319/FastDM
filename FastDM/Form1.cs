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
using Microsoft.Win32;

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
        public ThemeMode ThemeChoice { get; set; } = ThemeMode.System;   // System / Light / Dark
        public bool MinimizeToTray { get; set; } = false;
        public bool CloseToTray { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
        public bool CheckUpdatesOnStart { get; set; } = true;
        public int SpeedLimitKBps { get; set; } = 0;                    // 0 = আনলিমিটেড
        public ProxyMode Proxy { get; set; } = ProxyMode.System;       // None / System / Manual
        public ProxyKind ProxyType { get; set; } = ProxyKind.Http;     // Http / Socks5
        public string ProxyHost { get; set; } = "";
        public int ProxyPort { get; set; } = 8080;
        public string ProxyUser { get; set; } = "";                    // পাসওয়ার্ড Credential Manager-এ থাকে
    }

    public class DownloadItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Url { get; set; }
        public string FileName { get; set; }
        public string Folder { get; set; }
        public long TotalBytes { get; set; }
        public bool SupportsRange { get; set; }
        public bool NeedsProbe { get; set; }            // [এডিট ২] ফোল্ডার ডাউনলোডের আইটেম: শুরুর সময় সাইজ/resume জানবে
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
    public static partial class Engine     // [এডিট ১] partial
    {
        static volatile HttpClient Http = CreateClient();

        // প্রক্সি বদলালে নতুন ক্লায়েন্ট (চলমান রিকোয়েস্ট পুরোনোটাতেই শেষ হবে)
        public static void ResetClient() { Http = CreateClient(); }

        static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy = DynamicProxy.Instance,
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
            // [এডিট ৩] ftp/sftp হলে আলাদা প্রোব
            if (!IsHttp(it.Url)) { await RemoteTransfer.ProbeAsync(it, ct); return; }

            using var req = new HttpRequestMessage(HttpMethod.Get, it.Url);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            ApplyAuth(req);                                                   // [এডিট ৩]
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            CheckAuth(resp);                                                  // [এডিট ৩]
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
                    ApplyAuth(req);                                           // [এডিট ৪]

                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    CheckAuth(resp);                                          // [এডিট ৪]
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
                        await SpeedLimiter.ThrottleAsync(n, ct).ConfigureAwait(false);   // স্পিড লিমিটার
                        if (it.SupportsRange && seg.Start + seg.Downloaded > seg.End) break;
                    }

                    if (!it.SupportsRange) fs.SetLength(fs.Position);
                    if (it.SupportsRange && seg.Start + seg.Downloaded <= seg.End)
                        throw new IOException("Connection closed early.");

                    seg.Finished = true;
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (AuthRequiredException) { throw; }                      // [এডিট ৪] লগইন ভুল হলে retry নয়
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

            Controls.Add(new Label { Text = "URL(s) — one link per line (file or folder)", Location = new Point(16, 12), AutoSize = true });
            txtUrls = new TextBox { Location = new Point(16, 34), Size = new Size(528, 110), Multiline = true, ScrollBars = ScrollBars.Vertical, Text = initialUrl ?? "" };
            Controls.Add(txtUrls);

            Controls.Add(new Label { Text = "File name (optional, single link only)", Location = new Point(16, 156), AutoSize = true });
            txtName = new TextBox { Location = new Point(16, 178), Size = new Size(528, 26) };
            Controls.Add(txtName);

            Controls.Add(new Label { Text = "Save to", Location = new Point(16, 214), AutoSize = true });
            txtFolder = new TextBox { Location = new Point(16, 236), Size = new Size(436, 26), Text = folder };
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
                // [এডিট ৬] http, https, ftp, sftp
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) ||
                    !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ||
                      uri.Scheme == Uri.UriSchemeFtp || uri.Scheme == "sftp"))
                { MessageBox.Show(this, "Invalid link (http, https, ftp, sftp only):\n" + u); return false; }
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
        readonly ComboBox cmbTheme;
        readonly CheckBox chkMin, chkClose, chkNotify, chkUpd;
        readonly AppSettings s;

        public SettingsForm(AppSettings settings)
        {
            s = settings;
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(460, 396);
            Font = new Font("Segoe UI", 9.5f);

            Controls.Add(new Label { Text = "Connections per download (1–16)", Location = new Point(16, 20), AutoSize = true });
            numConn = new NumericUpDown { Location = new Point(320, 17), Width = 120, Minimum = 1, Maximum = 16, Value = Math.Clamp(s.Connections, 1, 16) };
            Controls.Add(numConn);

            Controls.Add(new Label { Text = "Simultaneous downloads (1–10)", Location = new Point(16, 60), AutoSize = true });
            numSim = new NumericUpDown { Location = new Point(320, 57), Width = 120, Minimum = 1, Maximum = 10, Value = Math.Clamp(s.MaxSimultaneous, 1, 10) };
            Controls.Add(numSim);

            Controls.Add(new Label { Text = "Default save folder", Location = new Point(16, 100), AutoSize = true });
            txtFolder = new TextBox { Location = new Point(16, 124), Width = 340, Text = s.DefaultFolder };
            Controls.Add(txtFolder);
            var browse = new Button { Text = "Browse…", Location = new Point(364, 122), Size = new Size(80, 28) };
            browse.Click += (a, b) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
            };
            Controls.Add(browse);

            Controls.Add(new Label { Text = "Theme", Location = new Point(16, 178), AutoSize = true });
            cmbTheme = new ComboBox { Location = new Point(300, 174), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTheme.Items.AddRange(new object[] { "Follow system", "Light", "Dark" });
            cmbTheme.SelectedIndex = Math.Clamp((int)s.ThemeChoice, 0, 2);
            Controls.Add(cmbTheme);

            chkMin = new CheckBox { Text = "Minimize to the system tray", Location = new Point(16, 216), AutoSize = true, Checked = s.MinimizeToTray };
            chkClose = new CheckBox { Text = "Close button keeps running in the tray", Location = new Point(16, 244), AutoSize = true, Checked = s.CloseToTray };
            chkNotify = new CheckBox { Text = "Show notifications", Location = new Point(16, 272), AutoSize = true, Checked = s.ShowNotifications };
            chkUpd = new CheckBox { Text = "Check for updates on startup (portable version)", Location = new Point(16, 300), AutoSize = true, Checked = s.CheckUpdatesOnStart };
            Controls.AddRange(new Control[] { chkMin, chkClose, chkNotify, chkUpd });

            var ok = new Button { Text = "Save", Location = new Point(250, 346), Size = new Size(90, 34) };
            var cancel = new Button { Text = "Cancel", Location = new Point(350, 346), Size = new Size(90, 34), DialogResult = DialogResult.Cancel };
            ok.Click += (a, b) =>
            {
                s.Connections = (int)numConn.Value;
                s.MaxSimultaneous = (int)numSim.Value;
                if (txtFolder.Text.Trim().Length > 0) s.DefaultFolder = txtFolder.Text.Trim();
                s.ThemeChoice = (ThemeMode)Math.Clamp(cmbTheme.SelectedIndex, 0, 2);
                s.MinimizeToTray = chkMin.Checked;
                s.CloseToTray = chkClose.Checked;
                s.ShowNotifications = chkNotify.Checked;
                s.CheckUpdatesOnStart = chkUpd.Checked;
                DialogResult = DialogResult.OK;
            };
            var net = new Button { Text = "Speed limit && proxy…", Location = new Point(16, 346), Size = new Size(200, 34) };
            net.Click += (a, b) =>
            {
                using var nf = new NetworkForm(s);
                Theme.Apply(nf);
                nf.ShowDialog(this);
            };
            Controls.Add(net);

            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    // ====================== মূল ফর্ম ======================
    public partial class Form1 : Form
    {
        // স্ট্যাটাস রঙ (দুই থিমেই একই), বাকি রঙ AppTheme.cs-এ
        static readonly Color Green = ColorTranslator.FromHtml("#2ECC71");
        static readonly Color Orange = ColorTranslator.FromHtml("#F5A623");
        static readonly Color Red = ColorTranslator.FromHtml("#FF5C77");
        static readonly Color Gray = ColorTranslator.FromHtml("#9AA0B4");
        static readonly Font BarFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        class IconTag { public string Glyph; public bool Accent; }

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
        ToolStripStatusLabel lblActive, lblSpeed, lblLimit;
        ToolStripDropDownButton speedMenu;
        System.Windows.Forms.Timer timer;
        int filter = 0;
        bool dirty;
        string lastClip;
        DateTime lastSave = DateTime.UtcNow;

        ToolStrip tools;
        StatusStrip statusBar;
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        bool exiting, trayTipShown;
        int sessionCompleted;
        string lastCompletedName = "";
        string lastTrayTip = "";
        DateTime lastErrorNotify = DateTime.MinValue;
        EventWaitHandle showEvent;
        RegisteredWaitHandle regWait;

        public Form1()
        {
            InitializeComponent();
            LoadState();
            Theme.SetMode(settings.ThemeChoice);
            NetworkApply.Load(settings);
            BuildUI();
            BuildTray();
            ApplyTheme();
            RebuildList();
            try { lastClip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null; } catch { }

            timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += OnTick;
            timer.Start();

            Activated += (s, e) => CheckClipboard();
            Resize += OnFormResize;
            FormClosing += OnFormClosing;
            FormClosed += (s, e) => Cleanup();
            SystemEvents.UserPreferenceChanged += OnSysPrefChanged;
            SetupSingleInstanceListener();

            Shown += (s, e) => FitLastColumn();
            // স্টার্টআপে নীরব আপডেট চেক (শুধু পোর্টেবল ভার্সনে কাজ করে)
            Shown += async (s, e) =>
            {
                if (settings.CheckUpdatesOnStart)
                {
                    try { await UpdateChecker.CheckAndShowAsync(this, true); } catch { }
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
                SmallImageList = new ImageList { ImageSize = new Size(1, 34) }   // row height
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
            lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            lv.DragEnter += OnDragEnter;
            lv.DragDrop += OnDragDrop;
            lv.ContextMenuStrip = BuildContextMenu();

            // Sidebar
            sidebar = new ListBox
            {
                Dock = DockStyle.Left,
                Width = 200,
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
            statusBar = new StatusStrip { SizingGrip = false };
            lblActive = new ToolStripStatusLabel("Active: 0") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            lblLimit = new ToolStripStatusLabel("Limit: Off");
            lblSpeed = new ToolStripStatusLabel("↓ 0 B/s");
            statusBar.Items.Add(lblActive);
            statusBar.Items.Add(lblLimit);
            statusBar.Items.Add(lblSpeed);

            // Toolbar (আইকন সহ; আইকন ফন্ট না থাকলে শুধু লেখা)
            tools = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(8, 6, 8, 6),
                Font = new Font("Segoe UI", 10f),
                ImageScalingSize = new Size(20, 20)
            };
            tools.Items.Add(Btn("Add URL", "＋ ", "\uE710", true, (s, e) => ShowAddDialog("")));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Btn("Resume", "▶ ", "\uE768", false, (s, e) => ResumeSelected()));
            tools.Items.Add(Btn("Pause", "❚❚ ", "\uE769", false, (s, e) => PauseSelected()));
            tools.Items.Add(Btn("Pause All", "", "\uE769", false, (s, e) => PauseAll()));
            tools.Items.Add(Btn("Remove", "✕ ", "\uE74D", false, (s, e) => RemoveSelected()));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Btn("Open File", "", "\uE8E5", false, (s, e) => OpenSelected(false)));
            tools.Items.Add(Btn("Open Folder", "", "\uE838", false, (s, e) => OpenSelected(true)));
            tools.Items.Add(new ToolStripSeparator());
            BuildSpeedMenu();
            tools.Items.Add(speedMenu);
            tools.Items.Add(Btn("Settings", "⚙ ", "\uE713", false, (s, e) => ShowSettings()));
            tools.Items.Add(Btn("Updates", "⟳ ", "\uE72C", false,
                async (s, e) => await UpdateChecker.CheckAndShowAsync(this, false)));

            // ক্রম গুরুত্বপূর্ণ: Fill আগে, তারপর বাকিগুলো
            Controls.Add(lv);
            Controls.Add(sidebar);
            Controls.Add(statusBar);
            Controls.Add(tools);
        }

        static ToolStripButton Btn(string text, string fallbackPrefix, string glyph, bool accent, EventHandler h)
        {
            bool icons = Icons.Available;
            var b = new ToolStripButton(icons ? text : fallbackPrefix + text)
            {
                DisplayStyle = icons ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(4),
                Tag = new IconTag { Glyph = glyph, Accent = accent }
            };
            b.Click += h;
            return b;
        }

        void RefreshToolIcons()
        {
            if (!Icons.Available) return;
            foreach (ToolStripItem b in tools.Items)
            {
                if (b.Tag is IconTag tag)
                {
                    var old = b.Image;
                    b.Image = Icons.Make(tag.Glyph, tag.Accent ? Theme.P.Accent : Theme.P.Text, 20);
                    old?.Dispose();
                }
            }
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
            var p = Theme.P;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? p.SideSel : p.SideBg)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(p.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);

            var tr = new Rectangle(e.Bounds.X + 18, e.Bounds.Y, e.Bounds.Width - 70, e.Bounds.Height);
            TextRenderer.DrawText(g, FilterNames[e.Index], Font, tr, p.SideText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            int count = items.Count(i => Match(e.Index, i));
            var cr = new Rectangle(e.Bounds.Right - 55, e.Bounds.Y, 45, e.Bounds.Height);
            TextRenderer.DrawText(g, count.ToString(), Font, cr, p.SideCount,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        void DrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            var p = Theme.P;
            using (var b = new SolidBrush(p.Header)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var pen = new Pen(p.Border))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 6, e.Bounds.Right - 1, e.Bounds.Bottom - 6);
            }
            var tr = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, tr, p.SubText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void DrawSub(object sender, DrawListViewSubItemEventArgs e)
        {
            var p = Theme.P;
            var it = (DownloadItem)e.Item.Tag;
            var g = e.Graphics;
            var r = new Rectangle(e.Bounds.X, e.Bounds.Y, lv.Columns[e.ColumnIndex].Width, e.Bounds.Height);

            Color back = e.Item.Selected ? p.Selection
                       : (e.ItemIndex % 2 == 0 ? p.Window : p.RowAlt);
            using (var b = new SolidBrush(back)) g.FillRectangle(b, r);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            if (e.ColumnIndex == 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(StateColor(it.State)))
                    g.FillEllipse(b, r.X + 8, r.Y + (r.Height - 10) / 2, 10, 10);
                var tr = new Rectangle(r.X + 26, r.Y, r.Width - 30, r.Height);
                TextRenderer.DrawText(g, e.SubItem.Text, Font, tr, p.Text, flags);
            }
            else if (e.ColumnIndex == 2)
            {
                var bar = new Rectangle(r.X + 6, r.Y + (r.Height - 18) / 2, r.Width - 14, 18);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var gp = RoundRect(bar, 8))
                using (var b = new SolidBrush(p.Track))
                    g.FillPath(b, gp);

                string label;
                if (it.TotalBytes > 0)
                {
                    int w = (int)(bar.Width * it.Percent / 100.0);
                    if (w > 6)
                    {
                        var fill = new Rectangle(bar.X, bar.Y, w, bar.Height);
                        using (var gp = RoundRect(fill, 8))
                        using (var b = new SolidBrush(StateColor(it.State)))
                            g.FillPath(b, gp);
                    }
                    label = it.Percent.ToString("0.0") + "%";
                }
                else label = it.Downloaded > 0 ? Fmt(it.Downloaded) : "—";
                TextRenderer.DrawText(g, label, BarFont, bar, p.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }
            else
            {
                var tr = new Rectangle(r.X + 6, r.Y, r.Width - 8, r.Height);
                TextRenderer.DrawText(g, e.SubItem.Text, Font, tr, p.Text, flags);
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
                case DlState.Downloading: return Theme.P.Accent;
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

            int actNow = items.Count(i => i.State == DlState.Downloading);
            int queNow = items.Count(i => i.State == DlState.Queued);
            lblActive.Text = "Active: " + actNow + "   Queued: " + queNow;
            lblSpeed.Text = "↓ " + Fmt(total) + "/s";
            string limText = settings.SpeedLimitKBps > 0 ? "Limit: " + SpeedLimiter.Describe(settings.SpeedLimitKBps) : "Limit: Off";
            if (lblLimit.Text != limText) lblLimit.Text = limText;
            sidebar.Invalidate();

            // ট্রে টুলটিপে স্পিড
            string tip = actNow > 0 ? "FastDM — ↓ " + Fmt(total) + "/s" : "FastDM";
            if (tray != null && tip != lastTrayTip) { tray.Text = tip; lastTrayTip = tip; }

            // সব ডাউনলোড শেষ হলে একটাই নোটিফিকেশন
            if (sessionCompleted > 0 && actNow == 0 && queNow == 0)
            {
                Notify("Download complete",
                       sessionCompleted == 1 ? lastCompletedName : sessionCompleted + " downloads completed");
                sessionCompleted = 0;
            }

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
                await Engine.RunItemAsync(it, settings.Connections, cts.Token);   // [এডিট ৫]
                if (it.TotalBytes <= 0) it.TotalBytes = it.Downloaded;
                it.State = DlState.Completed;
            }
            catch (OperationCanceledException) { it.State = DlState.Paused; }
            catch (Exception ex) { it.State = DlState.Error; it.Error = ex.Message; }
            finally { it.Cts = null; cts.Dispose(); }

            if (!it.RemoveRequested)
            {
                if (it.State == DlState.Completed) { sessionCompleted++; lastCompletedName = it.FileName; }
                else if (it.State == DlState.Error) NotifyError(it);
            }

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

        void ResumeAll()
        {
            foreach (var it in items)
                if (it.State == DlState.Paused || it.State == DlState.Error)
                { it.State = DlState.Queued; it.Error = null; }
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
        // [এডিট ৭] ফাইল আর ফোল্ডার লিঙ্ক আলাদা করে হ্যান্ডেল
        async void ShowAddDialog(string url)
        {
            using var dlg = new AddUrlForm(url, settings.DefaultFolder);
            Theme.Apply(dlg);
            var result = dlg.ShowDialog(this);
            if (result != DialogResult.OK && result != DialogResult.Yes) return;

            bool startNow = result == DialogResult.OK;
            string[] urls = dlg.Urls;
            string nameText = dlg.FileNameText;
            string folder = dlg.Folder;

            // ১) কোনটা ফাইল আর কোনটা ফোল্ডার, আলাদা করা
            lblActive.Text = "Checking link(s)…";
            var fileUrls = new List<string>();
            var folderUris = new List<Uri>();
            foreach (var u in urls)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    var (isFolder, resolved) = await Engine.DetectFolderAsync(new Uri(u), cts.Token);
                    if (isFolder) { folderUris.Add(resolved); continue; }
                }
                catch { /* ডিটেক্ট না হলে ফাইল ধরে নেবে */ }
                fileUrls.Add(u);
            }

            // ২) ফাইল লিঙ্ক: আগের মতোই অটো ডাউনলোড (লগইন লাগলে চাইবে)
            lblActive.Text = "Fetching file info…";
            foreach (var u in fileUrls)
            {
                var it = new DownloadItem
                {
                    Url = u,
                    Folder = folder,
                    FileName = fileUrls.Count == 1 && folderUris.Count == 0 && nameText.Length > 0
                               ? Engine.Sanitize(nameText) : ""
                };

                bool retry = true;
                while (retry)
                {
                    retry = false;
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                        await Engine.ProbeAsync(it, cts.Token);
                    }
                    catch (AuthRequiredException)
                    {
                        if (CredentialForm.Prompt(this, new Uri(u))) retry = true;
                    }
                    catch { /* ইনফো না পেলেও যোগ হবে; আসল এরর ডাউনলোডের সময় দেখাবে */ }
                }

                if (string.IsNullOrWhiteSpace(it.FileName)) it.FileName = Engine.NameFromUrl(u);
                it.FileName = UniqueName(folder, it.FileName);
                it.State = startNow ? DlState.Queued : DlState.Paused;
                items.Add(it);
            }
            MarkChanged();
            SaveState();

            // ৩) ফোল্ডার লিঙ্ক: উইন্ডো খুলবে (নাম, লোকেশন, ট্রি, ফিল্টার)
            foreach (var fu in folderUris) AddFolder(fu, startNow);
        }

        // [এডিট ৭] ফোল্ডার ডাউনলোড: ডায়ালগ থেকে বাছাই করা ফাইলগুলো কিউতে যোগ
        void AddFolder(Uri uri, bool startNow)
        {
            using var f = new FolderDownloadForm(uri, settings.DefaultFolder, settings.MaxSimultaneous);
            Theme.Apply(f);
            if (f.ShowDialog(this) != DialogResult.OK) return;

            settings.MaxSimultaneous = f.Simultaneous;

            foreach (var pf in f.Files)
            {
                string dir = pf.RelDir.Length == 0 ? f.TargetFolder : Path.Combine(f.TargetFolder, pf.RelDir);
                var it = new DownloadItem
                {
                    Url = pf.Url,
                    Folder = dir,
                    FileName = UniqueName(dir, Engine.Sanitize(pf.Name)),
                    TotalBytes = pf.Size,
                    NeedsProbe = Engine.IsHttp(pf.Url),     // http-তে সাইজ/resume ডাউনলোড শুরুর সময় জানবে
                    State = startNow ? DlState.Queued : DlState.Paused
                };
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
            Theme.Apply(f);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                Theme.SetMode(settings.ThemeChoice);
                ApplyTheme();
                RebuildList();
            }
            SaveState();     // স্পিড/প্রক্সি ডায়ালগে বদল করে Settings Cancel করলেও সেভ হবে
        }

        // ---------- স্পিড লিমিট মেনু (টুলবার) ----------
        void BuildSpeedMenu()
        {
            bool icons = Icons.Available;
            speedMenu = new ToolStripDropDownButton(icons ? "Speed" : "⚡ Speed")
            {
                DisplayStyle = icons ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(4),
                Tag = new IconTag { Glyph = "\uE945", Accent = false }
            };

            foreach (int preset in new[] { 0, 100, 500, 1024, 2048, 5120 })
            {
                int kb = preset;
                var mi = new ToolStripMenuItem(SpeedLimiter.Describe(kb)) { Tag = kb };
                mi.Click += (s, e) => SetSpeedLimit(kb);
                speedMenu.DropDownItems.Add(mi);
            }
            speedMenu.DropDownItems.Add(new ToolStripSeparator());
            speedMenu.DropDownItems.Add("Custom limit & proxy…", null, (s, e) => ShowNetworkSettings());

            speedMenu.DropDownOpening += (s, e) =>
            {
                foreach (var mi in speedMenu.DropDownItems.OfType<ToolStripMenuItem>())
                    if (mi.Tag is int v) mi.Checked = v == settings.SpeedLimitKBps;
            };
        }

        void SetSpeedLimit(int kbps)
        {
            settings.SpeedLimitKBps = kbps;
            SpeedLimiter.SetKBps(kbps);
            SaveState();
        }

        void ShowNetworkSettings()
        {
            using var f = new NetworkForm(settings);
            Theme.Apply(f);
            f.ShowDialog(this);
            SaveState();
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
            Theme.StyleStrip(lv.ContextMenuStrip);
            Theme.StyleStrip(trayMenu);
            Theme.StyleStrip(speedMenu.DropDown);
            lblActive.ForeColor = p.SubText;
            lblSpeed.ForeColor = p.SubText;
            lblLimit.ForeColor = p.SubText;
            RefreshToolIcons();
            Theme.SetTitleBar(this);
            Theme.StyleScroll(lv);
            Theme.StyleScroll(sidebar);
            ResumeLayout(true);
            lv.Invalidate(true);
            sidebar.Invalidate();
            Invalidate(true);
        }

        void OnSysPrefChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || settings.ThemeChoice != ThemeMode.System) return;
            try
            {
                BeginInvoke(new Action(() => { Theme.SetMode(ThemeMode.System); ApplyTheme(); }));
            }
            catch { }
        }

        // নামের কলাম বাদে শেষ কলাম বাকি জায়গা ভরবে (ডার্ক মোডে ফাঁকা হেডার এড়াতে)
        void FitLastColumn()
        {
            if (lv == null || lv.Columns.Count == 0 || lv.ClientSize.Width <= 0) return;
            int used = 0;
            for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
            int w = Math.Max(120, lv.ClientSize.Width - used);
            var last = lv.Columns[lv.Columns.Count - 1];
            if (last.Width != w) last.Width = w;
        }

        // ---------- সিস্টেম ট্রে ----------
        void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open FastDM", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Pause all", null, (s, e) => PauseAll());
            trayMenu.Items.Add("Resume all", null, (s, e) => ResumeAll());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, (s, e) => ExitApp());

            Icon ico = null;
            try { ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            tray = new NotifyIcon
            {
                Icon = ico ?? SystemIcons.Application,
                Text = "FastDM",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            tray.DoubleClick += (s, e) => RestoreFromTray();
            tray.BalloonTipClicked += (s, e) => RestoreFromTray();
        }

        void HideToTray(bool showTip)
        {
            Hide();
            if (showTip && !trayTipShown)
            {
                trayTipShown = true;
                Notify("FastDM", "Still running in the system tray. Click the icon to open it.");
            }
        }

        void RestoreFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            exiting = true;
            Close();
        }

        void OnFormResize(object sender, EventArgs e)
        {
            FitLastColumn();
            if (tray != null && settings.MinimizeToTray && WindowState == FormWindowState.Minimized)
                HideToTray(false);
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                if (settings.CloseToTray)
                {
                    e.Cancel = true;
                    HideToTray(true);
                    return;
                }

                bool busy = items.Any(i => i.State == DlState.Downloading || i.State == DlState.Queued);
                if (busy)
                {
                    var r = MessageBox.Show(this,
                        "Downloads are still running.\n\n" +
                        "Yes = keep running in the system tray\n" +
                        "No = exit (downloads will be paused)\n" +
                        "Cancel = stay",
                        "FastDM", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                    if (r == DialogResult.Yes) { e.Cancel = true; HideToTray(true); return; }
                }
            }

            foreach (var it in items) it.Cts?.Cancel();
            SaveState();
        }

        void Cleanup()
        {
            try { SystemEvents.UserPreferenceChanged -= OnSysPrefChanged; } catch { }
            try { timer?.Stop(); } catch { }
            try { regWait?.Unregister(null); showEvent?.Dispose(); } catch { }
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
            trayMenu?.Dispose();
        }

        // দ্বিতীয়বার অ্যাপ চালালে প্রথম উইন্ডোটা সামনে আনবে (Program.cs এর সাথে কাজ করে)
        void SetupSingleInstanceListener()
        {
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FastDM.Show");
                regWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (state, timedOut) =>
                {
                    try
                    {
                        if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(RestoreFromTray));
                    }
                    catch { }
                }, null, Timeout.Infinite, false);
            }
            catch { }
        }

        // ---------- নোটিফিকেশন ----------
        void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (tray == null || !settings.ShowNotifications) return;
            if (ActiveForm != null) return;      // অ্যাপ সামনে থাকলে নোটিফিকেশন দরকার নেই
            try
            {
                if (title.Length > 60) title = title.Substring(0, 60);
                if (text.Length > 240) text = text.Substring(0, 240) + "…";
                tray.ShowBalloonTip(4000, title, text, icon);
            }
            catch { }
        }

        void NotifyError(DownloadItem it)
        {
            if ((DateTime.UtcNow - lastErrorNotify).TotalSeconds < 10) return;   // স্প্যাম ঠেকাতে
            lastErrorNotify = DateTime.UtcNow;
            Notify("Download failed", it.FileName + "\n" + (it.Error ?? ""), ToolTipIcon.Error);
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
