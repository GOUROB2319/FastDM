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
                T.Check("cookies.txt arguments contain the selected file path only", YtDlpDiagnostics.CookieArguments(file).SequenceEqual(new[] { "--cookies", valid }));

                var missing = YtDlpDiagnostics.SelectCookieSource(Path.Combine(dir, "missing.txt"), "chrome");
                T.Check("a missing configured file is an error, never a browser fallback", missing.Kind == YtDlpCookieSourceKind.MissingCookieFile && YtDlpDiagnostics.Classify("error", missing) == YtDlpFailureKind.MissingCookieFile);

                string invalid = Path.Combine(dir, "invalid.txt");
                File.WriteAllText(invalid, "not a cookie file\n");
                T.Check("a wrong file header is rejected without exposing its path", YtDlpDiagnostics.SelectCookieSource(invalid, "edge").Kind == YtDlpCookieSourceKind.InvalidCookieFile);
                string empty = Path.Combine(dir, "empty.txt");
                File.WriteAllText(empty, "# Netscape HTTP Cookie File\n# empty\n");
                T.Check("an empty cookies.txt file is rejected", YtDlpDiagnostics.SelectCookieSource(empty, "edge").Kind == YtDlpCookieSourceKind.InvalidCookieFile);

                var none = YtDlpDiagnostics.SelectCookieSource("", "");
                T.Check("None cookie mode adds no cookie arguments", none.Kind == YtDlpCookieSourceKind.None && YtDlpDiagnostics.CookieArguments(none).Count == 0);
                var brave = YtDlpDiagnostics.SelectCookieSource("", "brave");
                T.Check("Brave selection adds only the expected browser arguments", YtDlpDiagnostics.CookieArguments(brave).SequenceEqual(new[] { "--cookies-from-browser", "brave" }));
                var injectedBrowser = YtDlpDiagnostics.SelectCookieSource("", "brave --cookies C:\\private.txt");
                T.Check("invalid state.json browser text cannot inject yt-dlp arguments", injectedBrowser.Kind == YtDlpCookieSourceKind.None && YtDlpDiagnostics.CookieArguments(injectedBrowser).Count == 0);
                T.Check("explicit cookie-free retry skips even an invalid configured source", YtDlpDiagnostics.CookieArguments(new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.InvalidCookieFile }, true).Count == 0);

                T.Check("Chrome database copy failure is diagnosed separately", YtDlpDiagnostics.Classify("Could not copy Chrome cookie database.") == YtDlpFailureKind.BrowserDatabaseLocked);
                T.Check("Brave cookie database not found is distinct from a locked database", YtDlpDiagnostics.Classify("ERROR: could not find brave cookies database in <user-path>\\AppData\\Local\\BraveSoftware\\Brave-Browser\\User Data") == YtDlpFailureKind.BrowserDatabaseNotFound);
                T.Check("cookie database permission denial is distinct from missing or locked database", YtDlpDiagnostics.Classify("Permission denied reading Chrome cookie database") == YtDlpFailureKind.BrowserDatabasePermissionDenied);
                T.Check("database-not-found guidance offers None and correct profile choices", YtDlpDiagnostics.Describe(YtDlpFailureKind.BrowserDatabaseNotFound, false).NextStep.Contains("Cookie source to None") && YtDlpDiagnostics.CanRetryWithoutCookies(YtDlpFailureKind.BrowserDatabaseNotFound));
                T.Check("database-lock guidance asks to close browser background processes", YtDlpDiagnostics.Describe(YtDlpFailureKind.BrowserDatabaseLocked, false).NextStep.Contains("background processes"));
                T.Check("YouTube bot verification is diagnosed separately", YtDlpDiagnostics.Classify("Sign in to confirm you’re not a bot.") == YtDlpFailureKind.BotVerification);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
