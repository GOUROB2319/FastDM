#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FastDM
{
    // Delete বাটন চাপলে কী হবে
    public enum DeleteAction { Ask, RemoveOnly, DeleteFiles }

    // একই নামের ফাইল আগে থেকে থাকলে কী হবে
    public enum FileExistsAction { Rename, Overwrite, Ask }

    // "Suggest folders based on file type" এর ক্যাটাগরি
    public static class FileCategories
    {
        static readonly Dictionary<string, string> Map = BuildMap();

        static Dictionary<string, string> BuildMap()
        {
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Add(string cat, string exts)
            {
                foreach (var e in exts.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    m["." + e] = cat;
            }

            Add("Video", "mp4 mkv avi mov wmv flv webm m4v ts mpg mpeg 3gp ogv");
            Add("Music", "mp3 m4a aac flac wav ogg opus wma");
            Add("Documents", "pdf doc docx xls xlsx ppt pptx txt rtf odt ods epub csv");
            Add("Compressed", "zip rar 7z tar gz bz2 xz");
            Add("Programs", "exe msi apk iso dmg msix appx bin");
            Add("Images", "jpg jpeg png gif bmp webp svg tif tiff ico");
            return m;
        }

        public static string Of(string fileName)
        {
            string ext = Path.GetExtension(fileName ?? "");
            return Map.TryGetValue(ext, out var c) ? c : "Other";
        }
    }

    // সমস্যা খুঁজতে সাধারণ লগ ফাইল। বন্ধ থাকলে কিছুই লেখে না।
    public static class AppLog
    {
        public static bool Enabled { get; set; }

        static readonly object gate = new object();

        public static string Folder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FastDM", "logs");

        public static void Write(string line)
        {
            if (!Enabled) return;

            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Folder);

                    string file = Path.Combine(Folder, "fastdm-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);

                    TrimOld();
                }
            }
            catch { }
        }

        static DateTime lastTrim = DateTime.MinValue;

        // সর্বোচ্চ ৭টা লগ ফাইল রাখে (দিনে একবার পরিষ্কার)
        static void TrimOld()
        {
            if ((DateTime.UtcNow - lastTrim).TotalHours < 12) return;
            lastTrim = DateTime.UtcNow;

            var old = new DirectoryInfo(Folder).GetFiles("fastdm-*.log")
                .OrderByDescending(f => f.Name).Skip(7);

            foreach (var f in old)
            {
                try { f.Delete(); } catch { }
            }
        }
    }

    // ডাউনলোড শেষ হলে ফাইলের ওপর কাজ: সার্ভারের সময়, Mark of the Web, অ্যান্টিভাইরাস, বাইরের অ্যাপ
    public static class PostDownload
    {
        public static void Apply(AppSettings s, DownloadItem it)
        {
            try
            {
                string path = it.SavePath;
                if (!File.Exists(path)) return;

                if (s.UseServerTime && it.ServerTime.HasValue)
                {
                    try { File.SetLastWriteTimeUtc(path, it.ServerTime.Value.UtcDateTime); } catch { }
                }

                if (s.MarkDownloaded)
                    MarkOfTheWeb(path, it.Url);

                if (s.AntivirusAuto)
                    RunTool(s.AntivirusPath, s.AntivirusArgs, path);

                if (s.RunAppOnComplete)
                    RunTool(s.CompleteAppPath, s.CompleteAppArgs, path);
            }
            catch (Exception ex)
            {
                AppLog.Write("PostDownload failed: " + ex.Message);
            }
        }

        // Windows-কে জানায় ফাইলটা ইন্টারনেট থেকে এসেছে (SmartScreen সতর্ক করতে পারে)
        public static void MarkOfTheWeb(string path, string url)
        {
            try
            {
                File.WriteAllText(
                    path + ":Zone.Identifier",
                    "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + PlaylistBuilder.StripCredentials(url ?? "") + "\r\n");
            }
            catch { /* FAT/exFAT ড্রাইভে সম্ভব নয়, চুপচাপ বাদ */ }
        }

        // %path% এর জায়গায় ফাইলের পাথ বসিয়ে প্রোগ্রাম চালায়
        public static void RunTool(string exe, string args, string path)
        {
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return;

            try
            {
                string a = (args ?? "").Replace("%path%", path);

                Process.Start(new ProcessStartInfo(exe, a)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                AppLog.Write("Ran: " + exe + " " + a);
            }
            catch (Exception ex)
            {
                AppLog.Write("Could not run " + exe + ": " + ex.Message);
            }
        }

        // Windows Defender-এর MpCmdRun.exe (সবচেয়ে নতুন Platform ভার্সন আগে)
        public static string FindDefender()
        {
            try
            {
                string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string platform = Path.Combine(pd, "Microsoft", "Windows Defender", "Platform");

                if (Directory.Exists(platform))
                {
                    var best = Directory.GetDirectories(platform)
                        .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                        .Select(d => Path.Combine(d, "MpCmdRun.exe"))
                        .FirstOrDefault(File.Exists);

                    if (best != null) return best;
                }

                string pf = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Windows Defender", "MpCmdRun.exe");

                return File.Exists(pf) ? pf : null;
            }
            catch { return null; }
        }

        public const string DefenderArgs = "-Scan -ScanType 3 -File \"%path%\"";
    }
}
