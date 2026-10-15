using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FastDM
{
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
            catch { /* the logger itself must never throw or log */ }
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
                try { f.Delete(); } catch { /* an old log that is in use stays */ }
            }
        }
    }
}
