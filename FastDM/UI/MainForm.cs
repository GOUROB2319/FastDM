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

        // ---------- toolbar: every button stays on one row ----------
        System.Windows.Forms.Timer? toolbarFitTimer;

        // Id of the download that was just added: the next list rebuild selects it, so the details panel shows it.
        string? selectNewId;

        // What the user removed, newest first. Shown under "Deleted" in the sidebar, kept in state.json.
        readonly List<DownloadItem> deleted = new List<DownloadItem>();

        const int DeletedFilter = 5;

        const int MaxDeletedHistory = 500;

        // ডিস্ক থেকে মুছে ফেলা ফাইল লিস্ট থেকেও সরানো (প্রতি ৩০ সেকেন্ডে একবার)
        DateTime lastMissingCheck =
            DateTime.MinValue;

        // ---------- Add / Settings ----------
        readonly SemaphoreSlim addGate =
            new SemaphoreSlim(1, 1);

        // ---------- সেভ / লোড ----------
        // Set by LoadState when state.json was damaged or newer: shown once after the window opens.
        string? startupNotice;

        // True when a damaged state.json could not be set aside: saving would destroy it, so we do not save.
        bool stateWriteBlocked;

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
