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
        public int MaxSimultaneous { get; set; } = 4;   // একসাথে কয়টা ফাইল
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
        public bool SchedulerEnabled { get; set; } = false;
        public int ScheduleDays { get; set; } = 0x7F;                  // বিটমাস্ক: Sun=bit0 ... Sat=bit6
        public int ScheduleStartMin { get; set; } = 120;               // 02:00
        public int ScheduleEndMin { get; set; } = 360;                 // 06:00
        public bool ScheduleStopOutside { get; set; } = true;          // উইন্ডো শেষে চলমান ডাউনলোড পজ
        public bool ShowDetails { get; set; } = true;
        // ---- Preferences (FDM-স্টাইল) ----
        public bool SuggestByType { get; set; } = false;       // ডিফল্ট ফোল্ডারের ভেতরে ফাইলের ধরন অনুযায়ী সাবফোল্ডার (Video, Music…)
        public bool SuggestByHost { get; set; } = false;       // ...এবং সাইটের নাম অনুযায়ী সাবফোল্ডার
        public bool CompactView { get; set; } = false;         // ডাউনলোড লিস্টে ছোট সারি
        public bool AutoRemoveMissing { get; set; } = true;    // ডিস্ক থেকে মুছে ফেলা ফাইল লিস্ট থেকেও সরাও
        public bool AutoRemoveCompleted { get; set; } = false; // শেষ হলেই লিস্ট থেকে সরাও (ফাইল থাকে)
        public bool AutoRetry { get; set; } = true;            // ব্যর্থ ডাউনলোড নিজে আবার চেষ্টা
        public int MaxRetries { get; set; } = 3;
        public bool SkipWebPages { get; set; } = true;         // লিঙ্কের উত্তর HTML পেজ হলে ডাউনলোড করো না
        public bool UseServerTime { get; set; } = false;       // ফাইলের তারিখ = সার্ভারের Last-Modified
        public bool MarkDownloaded { get; set; } = true;       // Mark of the Web (Windows SmartScreen)
        public int MaxBatchUrls { get; set; } = 100;           // একবারে সর্বোচ্চ কয়টা লিঙ্ক
        public bool NotifyAdded { get; set; } = false;
        public bool NotifyCompleted { get; set; } = true;
        public bool NotifyFailed { get; set; } = true;
        public string AntivirusPath { get; set; } = "";
        public string AntivirusArgs { get; set; } = "\"%path%\"";
        public bool AntivirusAuto { get; set; } = false;
        public bool RunAppOnComplete { get; set; } = false;
        public string CompleteAppPath { get; set; } = "";
        public string CompleteAppArgs { get; set; } = "\"%path%\"";
        public DeleteAction DeleteAction { get; set; } = DeleteAction.Ask;
        public FileExistsAction FileExists { get; set; } = FileExistsAction.Rename;
        public bool EnableLogging { get; set; } = false;

        // ---- Traffic Limits (Low / Medium / High) ----
        public TrafficMode ActiveMode { get; set; } = TrafficMode.High;
        public TrafficProfile LowProfile { get; set; } = TrafficProfile.DefaultFor(TrafficMode.Low);
        public TrafficProfile MediumProfile { get; set; } = TrafficProfile.DefaultFor(TrafficMode.Medium);
        public TrafficProfile HighProfile { get; set; } = TrafficProfile.DefaultFor(TrafficMode.High);
        public bool TrafficMigrated { get; set; } = false;     // পুরোনো একক গতি/কানেকশন সেটিং প্রিসেটে সরানো হয়েছে কি না
        public bool PauseSlow { get; set; } = true;            // ধীর ডাউনলোড সরিয়ে অন্যগুলোকে সুযোগ
        public int PauseSlowKBps { get; set; } = 0;
        public int PauseSlowMinutes { get; set; } = 10;
        public bool LaunchAtStartup { get; set; } = false;

        [JsonIgnore]
        public TrafficProfile ActiveProfile => ProfileOf(ActiveMode);

        public TrafficProfile ProfileOf(TrafficMode m)
        {
            var p = m == TrafficMode.Low ? LowProfile
                  : m == TrafficMode.Medium ? MediumProfile
                  : HighProfile;

            return p ?? TrafficProfile.DefaultFor(m);
        }

        // সক্রিয় মোডের মান বাকি কোডের পুরোনো ফিল্ডে (SpeedLimitKBps, Connections, MaxSimultaneous) বসায়
        public void ApplyActiveProfile()
        {
            LowProfile ??= TrafficProfile.DefaultFor(TrafficMode.Low);
            MediumProfile ??= TrafficProfile.DefaultFor(TrafficMode.Medium);
            HighProfile ??= TrafficProfile.DefaultFor(TrafficMode.High);

            LowProfile.Clamp();
            MediumProfile.Clamp();
            HighProfile.Clamp();

            var p = ActiveProfile;

            SpeedLimitKBps = p.SpeedKBps;
            Connections = p.PerServer;
            MaxSimultaneous = p.Simultaneous;
        }

        // "Reset": শুধু Preferences-এর মান ডিফল্টে। পেয়ার করা ব্রাউজার, সাইডবার, yt-dlp তারিখ আর শিডিউলার অক্ষত।
        public void ResetPreferences()
        {
            var d = new AppSettings();

            DefaultFolder = d.DefaultFolder;
            Connections = d.Connections;
            MaxSimultaneous = d.MaxSimultaneous;
            ThemeChoice = d.ThemeChoice;
            MinimizeToTray = d.MinimizeToTray;
            CloseToTray = d.CloseToTray;
            CheckUpdatesOnStart = d.CheckUpdatesOnStart;
            SpeedLimitKBps = d.SpeedLimitKBps;
            Proxy = d.Proxy;
            ProxyType = d.ProxyType;
            ProxyHost = d.ProxyHost;
            ProxyPort = d.ProxyPort;
            ProxyUser = d.ProxyUser;
            BridgeEnabled = d.BridgeEnabled;

            SuggestByType = d.SuggestByType;
            SuggestByHost = d.SuggestByHost;
            CompactView = d.CompactView;
            AutoRemoveMissing = d.AutoRemoveMissing;
            AutoRemoveCompleted = d.AutoRemoveCompleted;
            AutoRetry = d.AutoRetry;
            MaxRetries = d.MaxRetries;
            SkipWebPages = d.SkipWebPages;
            UseServerTime = d.UseServerTime;
            MarkDownloaded = d.MarkDownloaded;
            MaxBatchUrls = d.MaxBatchUrls;
            NotifyAdded = d.NotifyAdded;
            NotifyCompleted = d.NotifyCompleted;
            NotifyFailed = d.NotifyFailed;
            AntivirusPath = d.AntivirusPath;
            AntivirusArgs = d.AntivirusArgs;
            AntivirusAuto = d.AntivirusAuto;
            RunAppOnComplete = d.RunAppOnComplete;
            CompleteAppPath = d.CompleteAppPath;
            CompleteAppArgs = d.CompleteAppArgs;
            DeleteAction = d.DeleteAction;
            FileExists = d.FileExists;
            EnableLogging = d.EnableLogging;

            ActiveMode = d.ActiveMode;
            LowProfile = d.LowProfile;
            MediumProfile = d.MediumProfile;
            HighProfile = d.HighProfile;
            PauseSlow = d.PauseSlow;
            PauseSlowKBps = d.PauseSlowKBps;
            PauseSlowMinutes = d.PauseSlowMinutes;
            LaunchAtStartup = d.LaunchAtStartup;
            YtCookieBrowser = d.YtCookieBrowser;
            YtCookieFile = d.YtCookieFile;
        }

        public string YtCookieFile { get; set; } = "";                        // yt-dlp: path of an exported cookies.txt (used before the browser choice)
        public string YtCookieBrowser { get; set; } = "";                     // yt-dlp login cookies: "" | edge | chrome | firefox | brave
        public DateTime LastYtDlpUpdate { get; set; } = DateTime.MinValue;   // yt-dlp শেষ কবে আপডেট চেক হয়েছে
        public DateTime LastYtDlpAttempt { get; set; } = DateTime.MinValue; // failed updates retry after a short backoff
        public bool SidebarOpen { get; set; } = true;                        // বাম সাইডবার খোলা না বন্ধ
        public bool BridgeEnabled { get; set; } = true;                      // ব্রাউজার এক্সটেনশনের সংযোগ চালু
        public Dictionary<string, string> BridgeTokenOrigins { get; set; } = new Dictionary<string, string>(); // token-hash -> extension origin it was paired with
        public List<string> BridgeTokens { get; set; } = new List<string>(); // পেয়ার করা এক্সটেনশনের টোকেন-হ্যাশ
    }

    public class DownloadItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        string url = "";
        public string Url
        {
            get => url;
            set
            {
                url = UrlCredential.Strip(value, out var user, out var pass);
                if (user != null)
                {
                    UrlHadCredentials = true;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                        CredStore.Set(uri, user, pass ?? "", remember: false);
                }
            }
        }

        [JsonIgnore]
        public bool UrlHadCredentials { get; private set; }

        // Extension cookies are request-scoped and must never be serialized into state.json.
        [JsonIgnore]
        public List<BridgeCookie>? Cookies { get; set; }
        public string FileName { get; set; } = "";
        public string Folder { get; set; } = "";
        public string? Referer { get; set; }             // এক্সটেনশন থেকে এলে: লিঙ্কটা যে পেজে ছিল
        public string? UserAgent { get; set; }           // এক্সটেনশন থেকে এলে: ব্রাউজারের User-Agent
        public string? ContentType { get; set; }         // প্রোবে সার্ভারের Content-Type (যেমন text/html)
        public DateTimeOffset? ServerTime { get; set; } // প্রোবে সার্ভারের Last-Modified

        [JsonIgnore]
        public int RetryCount { get; set; }             // অটো-রিট্রাই কতবার হলো (সেশনের জন্য)

        [JsonIgnore]
        public bool AutoRetrying { get; set; }          // এই শুরুটা অটো-রিট্রাইয়ের (ম্যানুয়াল রিজিউমে কাউন্টার শূন্য)

        [JsonIgnore]
        public DateTime? SlowSince { get; set; }        // কখন থেকে গতি সীমার নিচে (Pause slow downloads)

        [JsonIgnore]
        public bool RequeueAfterPause { get; set; }     // ধীর বলে সরানো হলো: পজের পর লাইনের শেষে Queued
        public long TotalBytes { get; set; }
        public bool SupportsRange { get; set; }
        public string? Validator { get; set; }             // strong ETag or Last-Modified used to guard ranged resume
        public bool PausedBySchedule { get; set; }      // শিডিউলার পজ করেছে, উইন্ডো খুললে আবার চলবে
        public bool NeedsProbe { get; set; }            // ফোল্ডার ডাউনলোডের আইটেম: শুরুর সময় সাইজ/resume জানবে
        public StreamSpec? Stream { get; set; }          // HLS/DASH ভিডিও আইটেম (সাধারণ ফাইলে null)
        public YtDlpSpec? YtDlp { get; set; }            // yt-dlp আইটেম (YouTube/TikTok/Facebook ইত্যাদি)
        public int StreamDone { get; set; }             // নামানো সেগমেন্ট
        public int StreamTotal { get; set; }            // মোট সেগমেন্ট
        public DlState State { get; set; }
        public string? Error { get; set; }
        public DateTime Added { get; set; } = DateTime.Now;
        public DateTime? DeletedAt { get; set; }         // set when the download moved to the Deleted list
        public string? DeletedReason { get; set; }       // why: removed from the list / file deleted from disk ...
        public List<Segment> Segments { get; set; } = new List<Segment>();

        long _downloaded;

        public long Downloaded
        {
            get => Interlocked.Read(ref _downloaded);
            set => Interlocked.Exchange(ref _downloaded, value);
        }

        public void AddDownloaded(long n) => Interlocked.Add(ref _downloaded, n);

        [JsonIgnore]
        public string SavePath => Path.Combine(Folder, FileName);

        [JsonIgnore]
        public string TempPath => SavePath + ".part";

        [JsonIgnore]
        public string PartsDir => SavePath + ".parts";

        [JsonIgnore]
        public string StatusNote { get; set; } = "";

        [JsonIgnore]
        public YtDlpStage ProgressStage { get; set; } = YtDlpStage.None;

        [JsonIgnore]
        public CancellationTokenSource? Cts { get; set; }

        [JsonIgnore]
        public bool RemoveRequested { get; set; }

        [JsonIgnore]
        public bool DeleteFile { get; set; }

        [JsonIgnore]
        public double Speed { get; set; }

        [JsonIgnore]
        public bool IgnoreSchedule { get; set; }

        [JsonIgnore]
        public System.Collections.Generic.List<double> SpeedHistory { get; } =
            new System.Collections.Generic.List<double>();

        [JsonIgnore]
        public long LastBytes { get; set; }

        [JsonIgnore]
        public DateTime LastTick { get; set; }

        [JsonIgnore]
        public double Percent =>
            Stream != null && StreamTotal > 0
                ? Math.Min(100.0, StreamDone * 100.0 / StreamTotal)
                : TotalBytes > 0
                    ? Math.Min(100.0, Downloaded * 100.0 / TotalBytes)
                    : 0;
    }

    public class AppData
    {
        public int SchemaVersion { get; set; }          // 0 = saved before versions existed; see StateStore.CurrentSchema
        public AppSettings Settings { get; set; } = new AppSettings();
        public List<DownloadItem> Items { get; set; } = new List<DownloadItem>();
        public List<DownloadItem> Deleted { get; set; } = new List<DownloadItem>();      // history shown under "Deleted"
    }
}
