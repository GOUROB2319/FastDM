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
    // ====================== ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â² ======================
    public enum DlState { Queued, Downloading, Paused, Completed, Error }

    public class Segment
    {
        public long Start { get; set; }
        public long End { get; set; }          // inclusive, -1 = ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾
        public long Downloaded { get; set; }
        public bool Finished { get; set; }
    }

    public class AppSettings
    {
        public string DefaultFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        public int Connections { get; set; } = 8;       // ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨
        public int MaxSimultaneous { get; set; } = 3;   // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²
        public ThemeMode ThemeChoice { get; set; } = ThemeMode.System;   // System / Light / Dark
        public bool MinimizeToTray { get; set; } = false;
        public bool CloseToTray { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
        public bool CheckUpdatesOnStart { get; set; } = true;
        public int SpeedLimitKBps { get; set; } = 0;                    // 0 = ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¡
        public ProxyMode Proxy { get; set; } = ProxyMode.System;       // None / System / Manual
        public ProxyKind ProxyType { get; set; } = ProxyKind.Http;     // Http / Socks5
        public string ProxyHost { get; set; } = "";
        public int ProxyPort { get; set; } = 8080;
        public string ProxyUser { get; set; } = "";                    // ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ Credential Manager-ÃƒÂ Ã‚Â¦Ã‚Â ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡
        public bool SchedulerEnabled { get; set; } = false;
        public int ScheduleDays { get; set; } = 0x7F;                  // ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢: Sun=bit0 ... Sat=bit6
        public int ScheduleStartMin { get; set; } = 120;               // 02:00
        public int ScheduleEndMin { get; set; } = 360;                 // 06:00
        public bool ScheduleStopOutside { get; set; } = true;          // ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â·ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã…â€œ
        public bool ShowDetails { get; set; } = true;
        // ---- Preferences (FDM-ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²) ----
        public bool SuggestByType { get; set; } = false;       // ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â§ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â€šÂ¬ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° (Video, MusicÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦)
        public bool SuggestByHost { get; set; } = false;       // ...ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã¢â‚¬Å¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â® ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â€šÂ¬ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°
        public bool CompactView { get; set; } = false;         // ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿
        public bool AutoRemoveMissing { get; set; } = false;   // ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“
        public bool AutoRemoveCompleted { get; set; } = false; // ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â· ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ (ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡)
        public bool AutoRetry { get; set; } = true;            // ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¥ ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â·ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾
        public int MaxRetries { get; set; } = 3;
        public bool SkipWebPages { get; set; } = true;         // ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â° HTML ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…â€œ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾
        public bool UseServerTime { get; set; } = false;       // ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬â€œ = ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° Last-Modified
        public bool MarkDownloaded { get; set; } = true;       // Mark of the Web (Windows SmartScreen)
        public int MaxBatchUrls { get; set; } = 100;           // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢
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

        // "Reset": ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â§ÃƒÂ Ã‚Â§Ã‚Â Preferences-ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¥Ã‚Â¤ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°, ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°, yt-dlp ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬â€œ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â·ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¥Ã‚Â¤
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
        }

        public DateTime LastYtDlpUpdate { get; set; } = DateTime.MinValue;   // yt-dlp ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â· ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â§Ã¢â‚¬Â¡
        public bool SidebarOpen { get; set; } = true;                        // ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â® ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬â€œÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â§
        public bool BridgeEnabled { get; set; } = true;                      // ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã¢â‚¬Å¡ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚Â
        public List<string> BridgeTokens { get; set; } = new List<string>(); // ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨-ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¶
    }

    public class DownloadItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Url { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Folder { get; set; } = "";
        public string? Referer { get; set; }             // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡: ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â²
        public string? UserAgent { get; set; }           // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡: ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° User-Agent
        public string? ContentType { get; set; }         // ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° Content-Type (ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¨ text/html)
        public DateTimeOffset? ServerTime { get; set; } // ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° Last-Modified

        [JsonIgnore]
        public int RetryCount { get; set; }             // ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹-ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ (ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯)

        [JsonIgnore]
        public bool AutoRetrying { get; set; }          // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹-ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° (ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã¢â‚¬Å¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯)
        public long TotalBytes { get; set; }
        public bool SupportsRange { get; set; }
        public bool PausedBySchedule { get; set; }      // ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã…â€œ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â§Ã¢â‚¬Â¡, ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ ÃƒÂ Ã‚Â¦Ã¢â‚¬â€œÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡
        public bool NeedsProbe { get; set; }            // ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â®: ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…â€œ/resume ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡
        public StreamSpec? Stream { get; set; }          // HLS/DASH ÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â® (ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â§ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â£ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ null)
        public YtDlpSpec? YtDlp { get; set; }            // yt-dlp ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â® (YouTube/TikTok/Facebook ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¦ÃƒÂ Ã‚Â¦Ã‚Â¿)
        public int StreamDone { get; set; }             // ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸
        public int StreamTotal { get; set; }            // ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸
        public DlState State { get; set; }
        public string? Error { get; set; }
        public DateTime Added { get; set; } = DateTime.Now;
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
        public AppSettings Settings { get; set; } = new AppSettings();
        public List<DownloadItem> Items { get; set; } = new List<DownloadItem>();
    }

    // ====================== ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¾ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¨ ======================
    public static partial class Engine
    {
        // ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â§ÃƒÂ Ã‚Â§Ã‚Â ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ (ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Âª ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â§ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼)ÃƒÂ Ã‚Â¥Ã‚Â¤
        // CookieContainer ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â§ÃƒÂ Ã‚Â§Ã‚Â ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼, ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¥Ã‚Â¤
        // (Http-ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡, ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â£ CreateClient ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡)
        static readonly CookieContainer Jar = new CookieContainer();

        static volatile HttpClient Http = CreateClient();

        // ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¦ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¨ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸
        public static void ResetClient()
        {
            Http = CreateClient();
        }

        static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy = DynamicProxy.Instance,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = true,
                CookieContainer = Jar
            };

            var c = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FastDM/1.0");

            return c;
        }

        // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹
        public static void AddCookies(List<BridgeCookie> cookies)
        {
            if (cookies == null) return;

            foreach (var c in cookies)
            {
                try
                {
                    var ck = new Cookie(
                        c.Name,
                        c.Value ?? "",
                        string.IsNullOrEmpty(c.Path) ? "/" : c.Path,
                        c.Domain)
                    {
                        Secure = c.Secure
                    };

                    Jar.Add(ck);
                }
                catch { /* ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‹â€ ÃƒÂ Ã‚Â¦Ã‚Â§ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¦ */ }
            }
        }

        // ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¬ Referer / User-Agent (ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡) ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹
        public static void ApplyItemHeaders(HttpRequestMessage req, DownloadItem it)
        {
            if (!string.IsNullOrEmpty(it.Referer) &&
                Uri.TryCreate(it.Referer, UriKind.Absolute, out var r))
                req.Headers.Referrer = r;

            if (!string.IsNullOrEmpty(it.UserAgent))
                req.Headers.TryAddWithoutValidation("User-Agent", it.UserAgent);
        }

        public static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Trim();
        }

        public static string NameFromUrl(string url)
        {
            try
            {
                string n = Uri.UnescapeDataString(
                    Path.GetFileName(new Uri(url).AbsolutePath));

                if (!string.IsNullOrWhiteSpace(n))
                    return Sanitize(n);
            }
            catch { }

            return "download.bin";
        }

        // ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…â€œ, resume ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â® ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯
        public static async Task ProbeAsync(DownloadItem it, CancellationToken ct)
        {
            // ftp/sftp ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¦ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¬
            if (!IsHttp(it.Url))
            {
                await RemoteTransfer.ProbeAsync(it, ct);
                return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, it.Url);
            req.Headers.Range = new RangeHeaderValue(0, 0);

            ApplyAuth(req);
            ApplyItemHeaders(req, it);

            using var resp = await Http.SendAsync(
                req,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            CheckAuth(resp);
            resp.EnsureSuccessStatusCode();

            it.ContentType = resp.Content.Headers.ContentType?.MediaType;
            it.ServerTime = resp.Content.Headers.LastModified;

            if (resp.StatusCode == HttpStatusCode.PartialContent &&
                resp.Content.Headers.ContentRange?.Length != null)
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
                string? name = cd?.FileNameStar ?? cd?.FileName;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    it.FileName = Sanitize(name.Trim('"'));
                }
                else
                {
                    var uri = resp.RequestMessage?.RequestUri;
                    it.FileName = NameFromUrl(
                        uri != null ? uri.ToString() : it.Url);
                }
            }
        }

        public static async Task RunAsync(
            DownloadItem it,
            int connections,
            CancellationToken ct)
        {
            Directory.CreateDirectory(it.Folder);

            bool fresh =
                it.Segments.Count == 0 ||
                !it.SupportsRange ||
                !File.Exists(it.TempPath) ||
                new FileInfo(it.TempPath).Length != it.TotalBytes;

            if (fresh)
            {
                it.Segments = new List<Segment>();
                it.Downloaded = 0;

                if (it.SupportsRange && it.TotalBytes > 0)
                {
                    int n = (int)Math.Max(
                        1,
                        Math.Min(
                            connections,
                            it.TotalBytes / (1 << 20)));

                    long size = it.TotalBytes / n;

                    for (int i = 0; i < n; i++)
                    {
                        long start = i * size;
                        long end = (i == n - 1)
                            ? it.TotalBytes - 1
                            : start + size - 1;

                        it.Segments.Add(new Segment
                        {
                            Start = start,
                            End = end
                        });
                    }
                }
                else
                {
                    it.Segments.Add(new Segment
                    {
                        Start = 0,
                        End = it.TotalBytes > 0
                            ? it.TotalBytes - 1
                            : -1
                    });
                }

                using (var fs = new FileStream(
                    it.TempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite))
                {
                    if (it.SupportsRange)
                        fs.SetLength(it.TotalBytes);
                }
            }

            using var linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct);

            var tasks = it.Segments
                .Where(s => !s.Finished)
                .Select(s => Task.Run(async () =>
                {
                    try
                    {
                        await SegmentAsync(
                            it,
                            s,
                            linked.Token).ConfigureAwait(false);
                    }
                    catch when (!ct.IsCancellationRequested)
                    {
                        linked.Cancel();
                        throw;
                    }
                }))
                .ToList();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                var faulted = tasks.FirstOrDefault(t => t.IsFaulted);

                throw faulted?.Exception?.GetBaseException()
                    ?? new IOException("Download failed.");
            }

            if (it.SupportsRange &&
                it.Segments.Any(s => !s.Finished))
            {
                throw new IOException("Download incomplete.");
            }

            if (File.Exists(it.SavePath))
                File.Delete(it.SavePath);

            File.Move(it.TempPath, it.SavePath);
        }

        static async Task SegmentAsync(
            DownloadItem it,
            Segment seg,
            CancellationToken ct)
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

                    using var req = new HttpRequestMessage(
                        HttpMethod.Get,
                        it.Url);

                    if (it.SupportsRange)
                        req.Headers.Range =
                            new RangeHeaderValue(pos, seg.End);

                    ApplyAuth(req);
                    ApplyItemHeaders(req, it);

                    using var resp = await Http.SendAsync(
                        req,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct).ConfigureAwait(false);

                    CheckAuth(resp);
                    resp.EnsureSuccessStatusCode();

                    if (it.SupportsRange &&
                        resp.StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new IOException(
                            "Server does not support resume.");
                    }

                    using var input =
                        await resp.Content.ReadAsStreamAsync(ct)
                            .ConfigureAwait(false);

                    using var fs = new FileStream(
                        it.TempPath,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        1 << 16,
                        true);

                    fs.Seek(pos, SeekOrigin.Begin);

                    var buf = new byte[1 << 16];
                    int n;

                    while ((n = await input.ReadAsync(
                               buf.AsMemory(),
                               ct).ConfigureAwait(false)) > 0)
                    {
                        if (it.SupportsRange)
                        {
                            long room =
                                seg.End -
                                (seg.Start + seg.Downloaded) +
                                1;

                            if (n > room)
                                n = (int)room;
                        }

                        await fs.WriteAsync(
                            buf.AsMemory(0, n),
                            ct).ConfigureAwait(false);

                        seg.Downloaded += n;
                        it.AddDownloaded(n);

                        await SpeedLimiter.ThrottleAsync(
                            n,
                            ct).ConfigureAwait(false);

                        if (it.SupportsRange &&
                            seg.Start + seg.Downloaded > seg.End)
                            break;
                    }

                    if (!it.SupportsRange)
                        fs.SetLength(fs.Position);

                    if (it.SupportsRange &&
                        seg.Start + seg.Downloaded <= seg.End)
                    {
                        throw new IOException(
                            "Connection closed early.");
                    }

                    seg.Finished = true;
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (AuthRequiredException)
                {
                    throw;
                }
                catch (Exception) when (++attempt < 6)
                {
                    await Task.Delay(
                        1500 * attempt,
                        ct).ConfigureAwait(false);
                }
            }
        }
    }

    // ====================== ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°-ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ ListView ======================
    class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
        }
    }

    class BufferedListBox : ListBox
    {
        public BufferedListBox()
        {
            DoubleBuffered = true;

            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw,
                true);
        }
    }

    // ====================== Add URL ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ ======================
    class AddUrlForm : Form
    {
        readonly TextBox txtUrls;
        readonly TextBox txtName;
        readonly TextBox txtFolder;

        public string[] Urls =>
            txtUrls.Lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

        public string FileNameText => txtName.Text.Trim();
        public string Folder => txtFolder.Text.Trim();

        public AddUrlForm(string initialUrl, string folder)
        {
            Text = "Add Download";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 336);
            Font = new Font("Segoe UI", 9.5f);

            Controls.Add(new Label
            {
                Text = "URL(s) ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â one link per line (file or folder)",
                Location = new Point(16, 12),
                AutoSize = true
            });

            txtUrls = new TextBox
            {
                Location = new Point(16, 34),
                Size = new Size(528, 110),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = initialUrl ?? ""
            };

            Controls.Add(txtUrls);

            Controls.Add(new Label
            {
                Text = "File name (optional, single link only)",
                Location = new Point(16, 156),
                AutoSize = true
            });

            txtName = new TextBox
            {
                Location = new Point(16, 178),
                Size = new Size(528, 26)
            };

            Controls.Add(txtName);

            Controls.Add(new Label
            {
                Text = "Save to",
                Location = new Point(16, 214),
                AutoSize = true
            });

            txtFolder = new TextBox
            {
                Location = new Point(16, 236),
                Size = new Size(436, 26),
                Text = folder
            };

            Controls.Add(txtFolder);

            var browse = new Button
            {
                Text = "BrowseÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦",
                Location = new Point(460, 235),
                Size = new Size(84, 28)
            };

            browse.Click += (s, e) =>
            {
                using var fb = new FolderBrowserDialog
                {
                    SelectedPath = txtFolder.Text
                };

                if (fb.ShowDialog(this) == DialogResult.OK)
                    txtFolder.Text = fb.SelectedPath;
            };

            Controls.Add(browse);

            var start = new Button
            {
                Text = "Start Download",
                Location = new Point(196, 284),
                Size = new Size(124, 34)
            };

            var later = new Button
            {
                Text = "Download Later",
                Location = new Point(326, 284),
                Size = new Size(120, 34)
            };

            var cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(452, 284),
                Size = new Size(92, 34),
                DialogResult = DialogResult.Cancel
            };

            start.Click += (s, e) =>
            {
                if (Check())
                    DialogResult = DialogResult.OK;
            };

            later.Click += (s, e) =>
            {
                if (Check())
                    DialogResult = DialogResult.Yes;
            };

            Controls.AddRange(new Control[]
            {
                start,
                later,
                cancel
            });

            AcceptButton = start;
            CancelButton = cancel;
        }

        bool Check()
        {
            if (Urls.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "Please enter at least one URL.");

                return false;
            }

            foreach (var u in Urls)
            {
                if (!Uri.TryCreate(
                        u,
                        UriKind.Absolute,
                        out var uri) ||
                    !(uri.Scheme == Uri.UriSchemeHttp ||
                      uri.Scheme == Uri.UriSchemeHttps ||
                      uri.Scheme == Uri.UriSchemeFtp ||
                      uri.Scheme == "sftp"))
                {
                    MessageBox.Show(
                        this,
                        "Invalid link (http, https, ftp, sftp only):\n" + u);

                    return false;
                }
            }

            if (Folder.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "Please choose a folder.");

                return false;
            }

            return true;
        }
    }

    // ====================== Settings ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ ======================
    // ====================== ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Å¡ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â® ======================
    public partial class Form1 : Form
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
            "Errors"
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

        // These controls are created by BuildUI/BuildTray during Form1 construction before use.
        BufferedListView lv = null!;
        ListBox sidebar = null!;

        ToolStripStatusLabel lblActive = null!;
        ToolStripStatusLabel lblSpeed = null!;
        ToolStripStatusLabel lblLimit = null!;

        ToolStripDropDownButton speedMenu = null!;
        ToolStripDropDownButton doneMenu = null!;

        System.Windows.Forms.Timer navTimer = null!;

        int navTarget = 200;

        const int SidebarWidth = 200;

        readonly int[] sideCounts = new int[5];

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

        NotifyIcon? tray;
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

        public Form1()
        {
            InitializeComponent();

            LoadState();

            AppLog.Enabled =
                settings.EnableLogging;

            Theme.SetMode(settings.ThemeChoice);
            NetworkApply.Load(settings);

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
            Text = "Fast DM ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Download Manager";
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
                    RemoveSelected();
            };

            lv.DragEnter += OnDragEnter;
            lv.DragDrop += OnDragDrop;

            lv.ContextMenuStrip = BuildContextMenu();

            lv.SelectedIndexChanged += (s, e) =>
                details?.SetItem(
                    Sel().FirstOrDefault());

            // ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¸ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²
            details = new DetailsPanel
            {
                Visible = settings.ShowDetails
            };

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
                new ToolStripStatusLabel("ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬Å“ 0 B/s");

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

            // ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¬ toolbar action ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ Icon + Text ÃƒÂ Ã‚Â¦Ã‚Â¦ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡
            tools.Items.Add(
                Btn(
                    "Menu",
                    "ÃƒÂ¢Ã‹Å“Ã‚Â°  ",
                    "\uE700",
                    false,
                    (s, e) => ToggleSidebar(),
                    false));

            tools.Items.Add(
                new ToolStripSeparator());

            tools.Items.Add(
                Btn(
                    "Add URL",
                    "ÃƒÂ¯Ã‚Â¼Ã¢â‚¬Â¹  ",
                    "\uE710",
                    true,
                    (s, e) => ShowAddDialog(""),
                    false));

            tools.Items.Add(
                new ToolStripSeparator());

            tools.Items.Add(
                Btn(
                    "Resume",
                    "ÃƒÂ¢Ã¢â‚¬â€œÃ‚Â¶  ",
                    "\uE768",
                    false,
                    (s, e) => ResumeSelected(),
                    false));

            tools.Items.Add(
                Btn(
                    "Pause",
                    "ÃƒÂ¢Ã‚ÂÃ…Â¡ÃƒÂ¢Ã‚ÂÃ…Â¡  ",
                    "\uE769",
                    false,
                    (s, e) => PauseSelected(),
                    false));

            tools.Items.Add(
                Btn(
                    "Pause All",
                    "ÃƒÂ¢Ã‚ÂÃ…Â¡ÃƒÂ¢Ã‚ÂÃ…Â¡  ",
                    "\uE769",
                    false,
                    (s, e) => PauseAll(),
                    false));

            tools.Items.Add(
                Btn(
                    "Remove",
                    "ÃƒÂ¢Ã…â€œÃ¢â‚¬Â¢  ",
                    "\uE74D",
                    false,
                    (s, e) => RemoveSelected(),
                    false));

            tools.Items.Add(
                new ToolStripSeparator());

            tools.Items.Add(
                Btn(
                    "Open File",
                    "ÃƒÂ¢Ã¢â‚¬â€œÃ‚Â£  ",
                    "\uE8E5",
                    false,
                    (s, e) => OpenSelected(false),
                    false));

            tools.Items.Add(
                Btn(
                    "Open Folder",
                    "ÃƒÂ¢Ã¢â‚¬â€œÃ‚Â°  ",
                    "\uE838",
                    false,
                    (s, e) => OpenSelected(true),
                    false));

            tools.Items.Add(
                new ToolStripSeparator());

            BuildSpeedMenu();
            tools.Items.Add(speedMenu);

            tools.Items.Add(
                Btn(
                    "Schedule",
                    "ÃƒÂ¢Ã‚ÂÃ‚Â°  ",
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
                    "ÃƒÂ¢Ã¢â‚¬â€œÃ‚Â¤  ",
                    "\uE946",
                    false,
                    (s, e) => ToggleDetails(),
                    false));

            tools.Items.Add(
                Btn(
                    "Settings",
                    "ÃƒÂ¢Ã…Â¡Ã¢â€žÂ¢  ",
                    "\uE713",
                    false,
                    (s, e) => ShowSettings(),
                    false));

            tools.Items.Add(
                Btn(
                    "Updates",
                    "ÃƒÂ¢Ã…Â¸Ã‚Â³  ",
                    "\uE72C",
                    false,
                    async (s, e) =>
                        await UpdateChecker
                            .CheckAndShowAsync(this, false),
                    false));

            // ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â® ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Å¡ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â£: Fill ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡, ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹
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

                // ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¬ button-ÃƒÂ Ã‚Â¦Ã‚Â ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ icon + text
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

            return m;
        }

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Å¡ ----------
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
            if (e.Item?.Tag is not DownloadItem it)
                return;

            var p = Theme.P;
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
                    StateColor(it.State)))
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
                                   StateColor(it.State)))
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
                            : "ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â";
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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ----------
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

            lv.Items.Clear();

            foreach (var it in items
                .Where(i => Match(filter, i))
                .OrderByDescending(i => i.Added))
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

        void FillRow(
            ListViewItem l,
            DownloadItem it)
        {
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
                    : "ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â");

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

        // ---------- ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°: ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â° + ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ ----------
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
                "ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬Å“ " +
                Fmt(total) +
                "/s";

            string limText =
                settings.SpeedLimitKBps > 0
                    ? "Limit: " +
                      SpeedLimiter.Describe(
                          settings.SpeedLimitKBps)
                    : "Limit: Off";

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

            UpdateSideCounts();
            sidebar.Invalidate();

            string tip =
                actNow > 0
                    ? "FastDM ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬Å“ " +
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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â² ----------
        async void StartDownload(
            DownloadItem it)
        {
            var cts =
                new CancellationTokenSource();

            it.Cts = cts;
            it.State = DlState.Downloading;
            it.Error = null;
            it.PausedBySchedule = false;

            // ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚Â/ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹-ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â¦Ã‚Â£ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¨ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡
            if (!it.AutoRetrying)
                it.RetryCount = 0;

            it.AutoRetrying = false;

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
                    " ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â " +
                    ex.Message);
            }
            finally
            {
                it.Cts = null;
                it.StatusNote = "";
                cts.Dispose();
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

        // ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â·ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã…â€œ: ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼, Mark of the Web, ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¸, ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Âª, ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹
        void FinishDownload(
            DownloadItem it)
        {
            it.RetryCount = 0;

            AppLog.Write(
                "Completed: " +
                it.SavePath);

            var s = settings;

            // ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â® UI ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾
            Task.Run(
                () => PostDownload.Apply(
                    s,
                    it));

            if (s.AutoRemoveCompleted)
                items.Remove(it);
        }

        // ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¥ ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â·ÃƒÂ Ã‚Â¦Ã‚Â£ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â·ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ (ÃƒÂ Ã‚Â§Ã‚Â«, ÃƒÂ Ã‚Â§Ã‚Â§ÃƒÂ Ã‚Â§Ã‚Â¦, ÃƒÂ Ã‚Â§Ã‚Â§ÃƒÂ Ã‚Â§Ã‚Â«ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡, ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¡ ÃƒÂ Ã‚Â§Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚Â¦)
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

        // ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ (ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â§Ã‚Â©ÃƒÂ Ã‚Â§Ã‚Â¦ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°)
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
                        !string.IsNullOrEmpty(i.Folder) &&
                        !string.IsNullOrEmpty(i.FileName) &&
                        !File.Exists(i.SavePath))
                    .ToList();

            if (gone.Count == 0)
                return;

            foreach (var g in gone)
                items.Remove(g);

            AppLog.Write(
                "Removed " +
                gone.Count +
                " missing file(s) from the list");

            MarkChanged();
            SaveState();
        }

        void ResumeSelected()
        {
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

            bool deleteDone = false;

            // Preferences ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Advanced ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Delete button action
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
                            "/select,\"" +
                            target +
                            "\"");
                    }
                    else if (
                        Directory.Exists(
                            it.Folder))
                    {
                        Process.Start(
                            "explorer.exe",
                            "\"" +
                            it.Folder +
                            "\"");
                    }
                }
                else
                {
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

        void OnDoubleClick()
        {
            var sel = Sel();

            if (sel.Count == 0)
                return;

            var it = sel[0];

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

        // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° "Download with FastDM": ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾, ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¥Ã‚Â¤
        // (ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢, ÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…â€œ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ yt-dlp ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¦ ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡, ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¬ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â·ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¬ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡)
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
            RequestContext? ctx)
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

            await ProcessUrlsAsync(
                urls,
                nameText,
                folder,
                startNow,
                ctx);
        }

        // Add ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡, ÃƒÂ Ã‚Â¦Ã‚Â¦ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Å¡ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã…â€œ
        async Task ProcessUrlsAsync(
            string[] urls,
            string nameText,
            string folder,
            bool startNow,
            RequestContext? ctx = null)
        {
            // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ (Preferences ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Downloads)
            int maxBatch = Math.Max(1, settings.MaxBatchUrls);

            if (urls.Length > maxBatch)
            {
                MessageBox.Show(
                    this,
                    "Only the first " + maxBatch +
                    " of " + urls.Length +
                    " links will be added.\n(You can change this limit in Preferences ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Downloads.)",
                    "FastDM");

                urls = urls.Take(maxBatch).ToArray();
            }

            lblActive.Text =
                "Checking link(s)ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦";

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

                if (YtDlpSites.IsSupported(u))
                {
                    ytItems.Add(u);
                    continue;
                }

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

            // ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢
            lblActive.Text =
                "Fetching file infoÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦";

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

                // "Do not download web pages": ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â° HTML ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…â€œ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¦ (.html ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾)
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
                addedCount++;
            }

            if (skippedPages > 0)
            {
                lblActive.Text =
                    "Skipped " + skippedPages +
                    " web page link(s). (Preferences ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Downloads ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Do not download web pages)";
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

            // ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°
            foreach (var fu in folderUris)
                AddFolder(
                    fu,
                    startNow);

            // ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾
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
                "Reading stream infoÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦";

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
                "Reading video infoÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦";

            UseWaitCursor = true;

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
                    UpdateChecker.IsPackaged
                        ? "\n\nIf this keeps happening, a newer app version (with an updated yt-dlp) may fix it."
                        : "\n\nTry Settings ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Update yt-dlp.";

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

        // ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â° <ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â®>.m3u8 (ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â­ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¥, ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ VLC-ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡)
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

        // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° "Create playlist": Add ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ (ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡)
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

        // "Suggest folders": ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â§ÃƒÂ Ã‚Â§Ã‚Â ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â­ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ (ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¦ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â§Ã¢â‚¬Å¡ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¤)
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

            // ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â² ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ Preferences ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¦ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â€šÂ¬: Rename / Overwrite / Ask
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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° / "ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¬ ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â· ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡" / ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¸ ----------
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
                        : "ÃƒÂ¢Ã‚ÂÃ‚Â»  When done")
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

        void ShowSettings()
        {
            using var f =
                new PreferencesForm(settings);

            Theme.Apply(f);

            if (f.ShowDialog(this) ==
                DialogResult.OK)
            {
                ApplyPreferences();
            }

            SaveState();
        }

        // Preferences ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â­ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã¢â€žÂ¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¦ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°
        void ApplyPreferences()
        {
            Theme.SetMode(
                settings.ThemeChoice);

            ApplyTheme();
            ApplyCompactView();

            AppLog.Enabled =
                settings.EnableLogging;

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

        // "Compact view of downloads list": ÃƒÂ Ã‚Â¦Ã¢â‚¬ÂºÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿
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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¡ ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã…Â¸ ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚Â ----------
        void BuildSpeedMenu()
        {
            bool icons =
                Icons.Available;

            speedMenu =
                new ToolStripDropDownButton(
                    icons
                        ? "Speed"
                        : "ÃƒÂ¢Ã…Â¡Ã‚Â¡  Speed")
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

            foreach (int preset in
                     new[]
                     {
                         0,
                         100,
                         500,
                         1024,
                         2048,
                         5120
                     })
            {
                int kb = preset;

                var mi =
                    new ToolStripMenuItem(
                        SpeedLimiter.Describe(kb))
                    {
                        Tag = kb
                    };

                mi.Click += (s, e) =>
                    SetSpeedLimit(kb);

                speedMenu.DropDownItems.Add(mi);
            }

            speedMenu.DropDownItems.Add(
                new ToolStripSeparator());

            speedMenu.DropDownItems.Add(
                "Custom limit & proxyÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦",
                null,
                (s, e) => ShowNetworkSettings());

            speedMenu.DropDownOpening += (s, e) =>
            {
                foreach (var mi in speedMenu.DropDownItems
                    .OfType<ToolStripMenuItem>())
                {
                    if (mi.Tag is int v)
                    {
                        mi.Checked =
                            v ==
                            settings.SpeedLimitKBps;
                    }
                }
            };
        }

        void SetSpeedLimit(int kbps)
        {
            settings.SpeedLimitKBps =
                kbps;

            SpeedLimiter.SetKBps(kbps);

            SaveState();
        }

        void ShowNetworkSettings()
        {
            using var f =
                new NetworkForm(settings);

            Theme.Apply(f);
            f.ShowDialog(this);

            SaveState();
        }

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â® ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ ----------
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
            Theme.StyleStrip(speedMenu.DropDown);
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

        // ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â® ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¦ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â· ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â® ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â­ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡
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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â® ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ----------
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
                    items.Count(
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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ bridge ----------
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
            readonly Form1 f;

            public BridgeHostAdapter(Form1 form)
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
                                    "FastDM ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â browser extension",
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

                    // ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â¿ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡, ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡/ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã¢â‚¬â€
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

        // ---------- fastdm:// ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â®ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ ----------
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
                // fastdm:// ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¼ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…â€œ ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡, ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã¢â‚¬Â ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Â°ÃƒÂ Ã‚Â¦Ã…â€œÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â° ÃƒÂ Ã‚Â¦Ã‚Â¹ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã‚Â ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬â€ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡
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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â«ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â¶ÃƒÂ Ã‚Â¦Ã‚Â¨ ----------
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
                        "ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦";

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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¯ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬â€-ÃƒÂ Ã‚Â¦Ã‚Â¡ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â¦Ã‚Âª + ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¡ ----------
        void OnDragEnter(
            object? s,
            DragEventArgs e)
        {
            e.Effect =
                (e.Data != null &&
                 e.Data.GetDataPresent(
                     DataFormats.Text))
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
        }

        void OnDragDrop(
            object? s,
            DragEventArgs e)
        {
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

        // ---------- ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã‚Â­ / ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¡ ----------
        void SaveState()
        {
            lastSave =
                DateTime.UtcNow;

            try
            {
                Directory.CreateDirectory(
                    dataDir);

                var json =
                    JsonSerializer.Serialize(
                        new AppData
                        {
                            Settings =
                                settings,
                            Items =
                                items.ToList()
                        },
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                string tmp =
                    DataFile + ".tmp";

                File.WriteAllText(
                    tmp,
                    json);

                File.Move(
                    tmp,
                    DataFile,
                    true);
            }
            catch { }
        }

        void LoadState()
        {
            try
            {
                if (!File.Exists(DataFile))
                    return;

                var data =
                    JsonSerializer.Deserialize<AppData>(
                        File.ReadAllText(
                            DataFile));

                if (data == null)
                    return;

                if (data.Settings != null)
                    settings =
                        data.Settings;

                // ÃƒÂ Ã‚Â¦Ã‚ÂªÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â°ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã¢â‚¬Â¹ "Show notifications" ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â§ ÃƒÂ Ã‚Â¦Ã‚Â¥ÃƒÂ Ã‚Â¦Ã‚Â¾ÃƒÂ Ã‚Â¦Ã¢â‚¬Â¢ÃƒÂ Ã‚Â¦Ã‚Â²ÃƒÂ Ã‚Â§Ã¢â‚¬Â¡ ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â¨ ÃƒÂ Ã‚Â¦Ã‚Â¤ÃƒÂ Ã‚Â¦Ã‚Â¿ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â¦Ã…Â¸ÃƒÂ Ã‚Â¦Ã‚Â¾ ÃƒÂ Ã‚Â¦Ã‚Â¸ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã¢â‚¬Â¡ÃƒÂ Ã‚Â¦Ã…Â¡ÃƒÂ Ã‚Â¦Ã¢â‚¬Å“ ÃƒÂ Ã‚Â¦Ã‚Â¬ÃƒÂ Ã‚Â¦Ã‚Â¨ÃƒÂ Ã‚Â§Ã‚ÂÃƒÂ Ã‚Â¦Ã‚Â§
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