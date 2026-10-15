using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FastDM
{
    // সমস্যা খুঁজতে সাধারণ লগ ফাইল। বন্ধ থাকলে কিছুই লেখে না।
    public static class AppLog
    {
        public static bool Enabled { get; set; }

        static readonly object gate = new object();
        static readonly Regex UrlRx = new Regex("(?i)\\b(?:https?|ftp|sftp)://[^\\s<>\"']+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex AuthRx = new Regex(@"(?i)\b(authorization|proxy-authorization)\s*:\s*(?:basic|bearer)?\s*[^\s,;]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex CookieRx = new Regex(@"(?i)\b(cookie|set-cookie|cookies)\s*[:=]\s*[^\r\n]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex SecretRx = new Regex("(?i)\\b(password|passwd|proxy[_-]?(?:password|pass)|token|access[_-]?token|refresh[_-]?token|client[_-]?secret|api[_-]?key)\\s*[:=]\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s,;]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Redact(string? line)
        {
            string value = line ?? string.Empty;
            value = UrlRx.Replace(value, match =>
            {
                string raw = match.Value;
                int end = raw.Length;
                while (end > 0 && ".,;)]}".Contains(raw[end - 1])) end--;
                string suffix = raw.Substring(end);
                string token = raw.Substring(0, end);
                if (!Uri.TryCreate(token, UriKind.Absolute, out var uri))
                    return Regex.Replace(token, @"(?i)(://)[^/?#@\s]*@", "$1") + suffix;

                var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty };
                string safe = builder.Uri.GetLeftPart(UriPartial.Path);
                if (!string.IsNullOrEmpty(uri.Query)) safe += "?<redacted>";
                if (!string.IsNullOrEmpty(uri.Fragment)) safe += "#<redacted>";
                return safe + suffix;
            });
            value = AuthRx.Replace(value, "$1: <redacted>");
            value = CookieRx.Replace(value, "$1: <redacted>");
            value = SecretRx.Replace(value, "$1=<redacted>");

            foreach (string privateRoot in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                value = value.Replace(privateRoot, "<user-path>", StringComparison.OrdinalIgnoreCase);
            }
            return value;
        }

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
                    File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss") + "  " + Redact(line) + Environment.NewLine);

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
