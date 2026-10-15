using FastDM;

namespace FastDM.Tests
{
    static class YtDlpDiagnosticsTests
    {
        public static void Run()
        {
            T.Section("yt-dlp cookie selection and diagnosis");
            string dir = Path.Combine(Path.GetTempPath(), "fastdm-cookies-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string valid = Path.Combine(dir, "cookies.txt");
                File.WriteAllText(valid, "# Netscape HTTP Cookie File\n.example.test\tTRUE\t/\tTRUE\t0\tsid\tvalue\n");
                var file = YtDlpDiagnostics.SelectCookieSource(valid, "chrome");
                T.Check("a valid cookies.txt explicitly wins over the browser choice", file.Kind == YtDlpCookieSourceKind.CookieFile && file.FilePath == valid);

                var missing = YtDlpDiagnostics.SelectCookieSource(Path.Combine(dir, "missing.txt"), "chrome");
                T.Check("a missing configured file is an error, never a browser fallback", missing.Kind == YtDlpCookieSourceKind.MissingCookieFile && YtDlpDiagnostics.Classify("error", missing) == YtDlpFailureKind.MissingCookieFile);

                string invalid = Path.Combine(dir, "invalid.txt");
                File.WriteAllText(invalid, "not a cookie file\n");
                T.Check("a wrong file header is rejected without exposing its path", YtDlpDiagnostics.SelectCookieSource(invalid, "edge").Kind == YtDlpCookieSourceKind.InvalidCookieFile);
                T.Check("browser selection is used when no file is configured", YtDlpDiagnostics.SelectCookieSource("", "edge").Browser == "edge");

                T.Check("Chrome database copy failure is diagnosed separately", YtDlpDiagnostics.Classify("Could not copy Chrome cookie database.") == YtDlpFailureKind.BrowserDatabaseLocked);
                T.Check("YouTube bot verification is diagnosed separately", YtDlpDiagnostics.Classify("Sign in to confirm you’re not a bot.") == YtDlpFailureKind.BotVerification);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
