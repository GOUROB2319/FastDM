using System;
using System.IO;

namespace FastDM
{
    public enum YtDlpCookieSourceKind { None, CookieFile, Browser, MissingCookieFile, InvalidCookieFile }
    public enum YtDlpFailureKind { MissingTool, MissingCookieFile, InvalidCookieFile, BrowserDatabaseLocked, LoginRequired, CookiesRejected, MissingRuntime, OutdatedTool, NetworkOrAccess, Generic }

    public sealed class YtDlpCookieSource
    {
        public YtDlpCookieSourceKind Kind { get; init; }
        public string? Browser { get; init; }
        public string? FilePath { get; init; }
        public string? Error { get; init; }
    }

    public static class YtDlpDiagnostics
    {
        public static YtDlpCookieSource SelectCookieSource(string? configuredFile, string? browser)
        {
            string path = (configuredFile ?? string.Empty).Trim().Trim('"');
            if (path.Length > 0)
            {
                try
                {
                    if (!File.Exists(path))
                        return new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.MissingCookieFile, Error = "The selected cookies.txt file was not found." };

                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                    string? header = reader.ReadLine()?.Trim();
                    if (header != "# HTTP Cookie File" && header != "# Netscape HTTP Cookie File")
                        return new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.InvalidCookieFile, Error = "The selected file is not a readable Netscape-format cookies.txt file." };

                    bool hasCookie = false;
                    string? row;
                    while ((row = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(row) || row.StartsWith("#", StringComparison.Ordinal) &&
                            !row.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string[] fields = row.Split('\t');
                        if (fields.Length != 7 || fields[0].Trim().Length == 0 || fields[2].Length == 0 || fields[2][0] != '/' ||
                            !(fields[1].Equals("TRUE", StringComparison.OrdinalIgnoreCase) || fields[1].Equals("FALSE", StringComparison.OrdinalIgnoreCase)) ||
                            !(fields[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase) || fields[3].Equals("FALSE", StringComparison.OrdinalIgnoreCase)) ||
                            !long.TryParse(fields[4], out _) || fields[5].Length == 0)
                            return new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.InvalidCookieFile, Error = "The selected file is not a readable Netscape-format cookies.txt file." };
                        hasCookie = true;
                    }
                    if (!hasCookie)
                        return new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.InvalidCookieFile, Error = "The selected cookies.txt file is empty." };

                    return new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.CookieFile, FilePath = path };
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    return new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.InvalidCookieFile, Error = "The selected cookies.txt file cannot be read." };
                }
            }

            string candidate = (browser ?? string.Empty).Trim().ToLowerInvariant();
            string selected = Array.IndexOf(new[] { "edge", "chrome", "firefox", "brave" }, candidate) >= 0 ? candidate : string.Empty;
            return selected.Length == 0
                ? new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.None }
                : new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.Browser, Browser = selected };
        }

        public static YtDlpFailureKind Classify(string? message, YtDlpCookieSource? source = null)
        {
            string text = (message ?? string.Empty).ToLowerInvariant();
            if (text.Contains("yt-dlp.exe was not found")) return YtDlpFailureKind.MissingTool;
            if (source?.Kind == YtDlpCookieSourceKind.MissingCookieFile) return YtDlpFailureKind.MissingCookieFile;
            if (source?.Kind == YtDlpCookieSourceKind.InvalidCookieFile) return YtDlpFailureKind.InvalidCookieFile;
            if (text.Contains("could not copy") && text.Contains("cookie database") ||
                text.Contains("permission denied") && text.Contains("cookie database")) return YtDlpFailureKind.BrowserDatabaseLocked;
            if (text.Contains("sign in to confirm") || text.Contains("not a bot") || text.Contains("login required")) return YtDlpFailureKind.LoginRequired;
            if (text.Contains("no usable cookies") || text.Contains("cookies are no longer valid") || text.Contains("failed to decrypt")) return YtDlpFailureKind.CookiesRejected;
            if (text.Contains("javascript runtime") || text.Contains("no supported javascript runtime")) return YtDlpFailureKind.MissingRuntime;
            if (text.Contains("yt-dlp version is too old") || text.Contains("please update yt-dlp") || text.Contains("outdated yt-dlp")) return YtDlpFailureKind.OutdatedTool;
            if (text.Contains("http error 403") || text.Contains("http error 429") || text.Contains("unable to download") || text.Contains("timed out") || text.Contains("network")) return YtDlpFailureKind.NetworkOrAccess;
            return YtDlpFailureKind.Generic;
        }

        public static (string Summary, string NextStep) Describe(YtDlpFailureKind kind, bool packaged)
        {
            return kind switch
            {
                YtDlpFailureKind.MissingTool => ("FastDM could not find yt-dlp.", "Install the app's supported yt-dlp tool in its Tools folder, then try again."),
                YtDlpFailureKind.MissingCookieFile => ("The selected cookies.txt file is missing.", "Choose an existing cookies.txt file in Preferences → Advanced → Video sites."),
                YtDlpFailureKind.InvalidCookieFile => ("The selected cookies.txt file could not be read or is not in Netscape format.", "Choose a readable cookies.txt file whose first line is a Netscape cookie-file header."),
                YtDlpFailureKind.BrowserDatabaseLocked => ("yt-dlp could not copy the browser's cookie database.", "Close the browser and its background processes, then retry. If that still fails, choose a cookies.txt file in video settings."),
                YtDlpFailureKind.LoginRequired => ("The site says this video needs a signed-in session.", "Sign in to the site in your browser, then select that browser or a fresh cookies.txt file in video settings."),
                YtDlpFailureKind.CookiesRejected => ("The site did not accept the available cookies.", "Sign in again and select a fresh cookie source. The site may still require another check."),
                YtDlpFailureKind.MissingRuntime => ("A JavaScript runtime required by yt-dlp is unavailable.", "Install the supported runtime next to yt-dlp or use an app package that includes it."),
                YtDlpFailureKind.OutdatedTool => ("This site may need a newer yt-dlp version.", packaged ? "Install a newer FastDM package." : "Try Update yt-dlp in Preferences → Advanced."),
                YtDlpFailureKind.NetworkOrAccess => ("The site could not be reached or denied the request.", "Check your connection and proxy settings. The site may also restrict this request."),
                _ => ("yt-dlp could not read this video.", packaged ? "Check the diagnostic details and update FastDM if a newer package is available." : "Check the diagnostic details and try Update yt-dlp in Preferences → Advanced.")
            };
        }
    }
}
