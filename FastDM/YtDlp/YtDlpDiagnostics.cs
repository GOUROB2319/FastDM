using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FastDM
{
    public enum YtDlpCookieSourceKind { None, CookieFile, Browser, MissingCookieFile, InvalidCookieFile }
    public enum YtDlpFailureKind
    {
        MissingTool, MissingCookieFile, InvalidCookieFile, BrowserDatabaseNotFound,
        BrowserDatabaseLocked, BrowserDatabasePermissionDenied,
        RateLimited, TemporaryNetwork, DnsConnection, LoginRequired, BotVerification,
        MissingVisitorData, PoTokenRequired, UnsupportedVideo, GeoRestricted,
        FormatUnavailable, MissingFfmpeg, MergeFailed, InsufficientDiskSpace,
        PermissionDenied, ExpiredMediaUrl, CookiesRejected, MissingRuntime,
        OutdatedTool, NetworkOrAccess, Cancelled, Generic
    }

    public sealed class YtDlpCookieSource
    {
        public YtDlpCookieSourceKind Kind { get; init; }
        public string? Browser { get; init; }
        public string? FilePath { get; init; }
        public string? Error { get; init; }
    }

    public static class YtDlpDiagnostics
    {
        public static List<string> CookieArguments(YtDlpCookieSource source, bool omitCookies = false)
        {
            var args = new List<string>();
            if (omitCookies) return args;
            if (source.Kind is YtDlpCookieSourceKind.MissingCookieFile or YtDlpCookieSourceKind.InvalidCookieFile)
                throw new InvalidOperationException(source.Error ?? "The selected cookies.txt file is invalid.");
            if (source.Kind == YtDlpCookieSourceKind.CookieFile)
            {
                args.Add("--cookies");
                args.Add(source.FilePath!);
            }
            else if (source.Kind == YtDlpCookieSourceKind.Browser)
            {
                args.Add("--cookies-from-browser");
                args.Add(source.Browser!);
            }
            return args;
        }

        public static bool CanRetryWithoutCookies(YtDlpFailureKind kind) =>
            kind is YtDlpFailureKind.MissingCookieFile or YtDlpFailureKind.InvalidCookieFile or
                YtDlpFailureKind.BrowserDatabaseNotFound or YtDlpFailureKind.BrowserDatabaseLocked or
                YtDlpFailureKind.BrowserDatabasePermissionDenied or YtDlpFailureKind.CookiesRejected;

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
            if (text.Contains("selected cookies.txt file was not found")) return YtDlpFailureKind.MissingCookieFile;
            if (text.Contains("not a readable netscape-format cookies.txt") || text.Contains("selected cookies.txt file is empty"))
                return YtDlpFailureKind.InvalidCookieFile;
            if (source?.Kind == YtDlpCookieSourceKind.MissingCookieFile) return YtDlpFailureKind.MissingCookieFile;
            if (source?.Kind == YtDlpCookieSourceKind.InvalidCookieFile) return YtDlpFailureKind.InvalidCookieFile;
            if (Regex.IsMatch(text, @"\b(?:could not find|cannot find|can't find|unable to find|no)\s+(?:[a-z0-9_-]+\s+)?cookies database\b") ||
                Regex.IsMatch(text, @"\b(?:cookie|cookies) database\b.{0,100}\bnot found\b"))
                return YtDlpFailureKind.BrowserDatabaseNotFound;
            if (text.Contains("permission denied") && text.Contains("cookie database"))
                return YtDlpFailureKind.BrowserDatabasePermissionDenied;
            if ((text.Contains("could not copy") || text.Contains("failed to copy") || text.Contains("unable to copy")) &&
                text.Contains("cookie database")) return YtDlpFailureKind.BrowserDatabaseLocked;
            if (text.Contains("sign in to confirm you're not a bot") || text.Contains("sign in to confirm you’re not a bot") ||
                text.Contains("confirm you're not a bot") || text.Contains("confirm you’re not a bot") ||
                text.Contains("captcha") || text.Contains("bot verification")) return YtDlpFailureKind.BotVerification;
            if (text.Contains("missing required visitor data") || text.Contains("unable to fetch gvs po token") && text.Contains("visitor data"))
                return YtDlpFailureKind.MissingVisitorData;
            if (text.Contains("po token") || text.Contains("proof of origin token")) return YtDlpFailureKind.PoTokenRequired;
            if (text.Contains("sign in to confirm") || text.Contains("login required") || text.Contains("authentication required"))
                return YtDlpFailureKind.LoginRequired;
            if (text.Contains("no usable cookies") || text.Contains("cookies are no longer valid") ||
                text.Contains("cookies expired") || text.Contains("expired cookies") || text.Contains("failed to decrypt")) return YtDlpFailureKind.CookiesRejected;
            if (text.Contains("javascript runtime") || text.Contains("no supported javascript runtime")) return YtDlpFailureKind.MissingRuntime;
            if (text.Contains("yt-dlp version is too old") || text.Contains("please update yt-dlp") || text.Contains("outdated yt-dlp")) return YtDlpFailureKind.OutdatedTool;
            if (Regex.IsMatch(text, @"\bhttp(?: error)?\s*429\b") || Regex.IsMatch(text, @"\bstatus(?: code)?\s*[:=]?\s*429\b"))
                return YtDlpFailureKind.RateLimited;
            if (Regex.IsMatch(text, @"\bhttp(?: error)?\s*(?:408|5\d\d)\b")) return YtDlpFailureKind.TemporaryNetwork;
            if (text.Contains("this video is unavailable") || text.Contains("video is private") ||
                text.Contains("has been removed") || text.Contains("video has been deleted") || text.Contains("http error 404") ||
                text.Contains("this content isn't available")) return YtDlpFailureKind.UnsupportedVideo;
            if (text.Contains("not available in your country") || text.Contains("geo-restricted") ||
                text.Contains("outside the allowed region")) return YtDlpFailureKind.GeoRestricted;
            if (text.Contains("requested format is not available") || text.Contains("no video formats found") ||
                text.Contains("format is not available")) return YtDlpFailureKind.FormatUnavailable;
            if (text.Contains("ffmpeg was not found") || text.Contains("ffmpeg is not installed") ||
                text.Contains("ffmpeg not found") || text.Contains("ffprobe and ffmpeg not found")) return YtDlpFailureKind.MissingFfmpeg;
            if (text.Contains("merger error") || text.Contains("unable to merge formats") ||
                text.Contains("postprocessing: error") || text.Contains("ffmpeg exited with code")) return YtDlpFailureKind.MergeFailed;
            if (text.Contains("no space left on device") || text.Contains("not enough space") ||
                text.Contains("disk full") || text.Contains("winerror 112")) return YtDlpFailureKind.InsufficientDiskSpace;
            if (text.Contains("permission denied") || text.Contains("access is denied") || text.Contains("winerror 5"))
                return YtDlpFailureKind.PermissionDenied;
            if (text.Contains("url has expired") || text.Contains("signature has expired") ||
                text.Contains("http error 403") && (text.Contains("expired") || text.Contains("signature")))
                return YtDlpFailureKind.ExpiredMediaUrl;
            if (text.Contains("temporary failure in name resolution") || text.Contains("failed to resolve") ||
                text.Contains("name or service not known") || text.Contains("getaddrinfo failed") ||
                text.Contains("winerror 11001")) return YtDlpFailureKind.DnsConnection;
            if (text.Contains("timed out") || text.Contains("timeout") || text.Contains("connection reset") ||
                text.Contains("temporary network") || text.Contains("temporary failure")) return YtDlpFailureKind.TemporaryNetwork;
            if (text.Contains("http error 403") || text.Contains("unable to download") || text.Contains("network")) return YtDlpFailureKind.NetworkOrAccess;
            return YtDlpFailureKind.Generic;
        }

        public static TimeSpan? ParseRetryAfter(string? diagnostic, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(diagnostic)) return null;
            var match = Regex.Match(diagnostic, @"(?im)^\s*retry-after\s*:\s*(?<value>[^\r\n]+)\s*$");
            if (!match.Success) return null;
            string value = match.Groups["value"].Value.Trim();
            if (double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out double seconds))
                return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 300));
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                return TimeSpan.FromSeconds(Math.Clamp((date - now).TotalSeconds, 0, 300));
            return null;
        }

        public static string ProgressLabel(string? line)
        {
            string text = (line ?? "").ToLowerInvariant();
            if (text.Contains("downloading page") || text.Contains("playlist items")) return "Loading playlist entries…";
            if (text.Contains("downloading webpage")) return "Loading the video page…";
            if (text.Contains("downloading initial data") || text.Contains("player api json")) return "Reading video details…";
            if (text.Contains("javascript challenge") || text.Contains("solving js challenges")) return "Checking video playback details…";
            return "";
        }

        public static (string Summary, string NextStep) Describe(YtDlpFailureKind kind, bool packaged)
        {
            return kind switch
            {
                YtDlpFailureKind.MissingTool => ("FastDM could not find yt-dlp.", "Install the app's supported yt-dlp tool in its Tools folder, then try again."),
                YtDlpFailureKind.MissingCookieFile => ("The selected cookies.txt file is missing.", "Choose an existing cookies.txt file in Preferences → Advanced → Video sites."),
                YtDlpFailureKind.InvalidCookieFile => ("The selected cookies.txt file could not be read or is not in Netscape format.", "Choose a readable cookies.txt file whose first line is a Netscape cookie-file header."),
                YtDlpFailureKind.BrowserDatabaseNotFound => ("yt-dlp could not find the selected browser's cookie database. This does not show that the video itself is invalid.", "Open Video settings and choose Firefox or a readable exported cookies.txt file. For a public video you can instead set Cookie source to None and retry."),
                YtDlpFailureKind.BrowserDatabaseLocked => ("yt-dlp could not copy the browser's cookie database.", "Close the browser and its background processes. If it still fails, open Video settings and choose Firefox or an exported cookies.txt file."),
                YtDlpFailureKind.BrowserDatabasePermissionDenied => ("Windows denied access to the selected browser's cookie database.", "Check that the selected profile belongs to this Windows user. Otherwise open Video settings and choose Firefox or an exported cookies.txt file."),
                YtDlpFailureKind.RateLimited => ("The site is limiting requests from this network or session.", "Wait before trying again. Avoid repeated retries; reduce simultaneous video downloads. If the site asks for a check, complete it in your browser."),
                YtDlpFailureKind.TemporaryNetwork => ("The video service timed out or the connection was interrupted.", "Check the connection and proxy, then retry once the service is reachable."),
                YtDlpFailureKind.DnsConnection => ("FastDM could not resolve the video service address.", "Check the network or DNS settings, then retry."),
                YtDlpFailureKind.LoginRequired => ("The site says this video needs a signed-in session.", "Sign in to the site in your browser, then select that browser or a fresh cookies.txt file in video settings."),
                YtDlpFailureKind.BotVerification => ("The site requires a browser verification step.", "Complete the check in the browser. If you choose to use account cookies, enable that source in video settings. FastDM does not bypass the check."),
                YtDlpFailureKind.MissingVisitorData => ("YouTube did not provide the visitor data yt-dlp needs for this request.", "Wait and retry later. If the video requires an account, optionally select a signed-in browser or fresh cookies.txt file in video settings."),
                YtDlpFailureKind.PoTokenRequired => ("YouTube requires a Proof of Origin token for this format or client.", packaged ? "Update FastDM when a newer supported yt-dlp build is packaged. Do not install an unknown plugin." : "Update the supported yt-dlp tool. FastDM does not install third-party token providers automatically."),
                YtDlpFailureKind.UnsupportedVideo => ("This video is private, removed, or unavailable to this session.", "Open it in the browser to check its availability and access permissions."),
                YtDlpFailureKind.GeoRestricted => ("This video is unavailable in the current region.", "Check the video's availability in the browser. FastDM does not bypass region restrictions."),
                YtDlpFailureKind.FormatUnavailable => ("The selected video format is no longer available.", "Reopen the preview to refresh the available formats and choose another quality."),
                YtDlpFailureKind.MissingFfmpeg => ("FFmpeg is needed to combine the selected video and audio formats.", "Install a trusted FFmpeg build beside FastDM, or choose a format that does not need merging."),
                YtDlpFailureKind.MergeFailed => ("The video downloaded, but audio/video processing failed.", "Check that FFmpeg is available and that the destination has free space, then retry."),
                YtDlpFailureKind.InsufficientDiskSpace => ("There is not enough free space to finish this video.", "Choose a destination with more free space, then retry."),
                YtDlpFailureKind.PermissionDenied => ("FastDM cannot write to the selected destination.", "Choose a folder you can write to, then retry."),
                YtDlpFailureKind.ExpiredMediaUrl => ("The video link used for this download expired.", "FastDM will refresh the video information once. If it still fails, reopen the preview and try again."),
                YtDlpFailureKind.CookiesRejected => ("The site did not accept the available cookies.", "Sign in again and select a fresh cookie source. The site may still require another check."),
                YtDlpFailureKind.MissingRuntime => ("A JavaScript runtime required by yt-dlp is unavailable.", "Install the supported runtime next to yt-dlp or use an app package that includes it."),
                YtDlpFailureKind.OutdatedTool => ("This site may need a newer yt-dlp version.", packaged ? "Install a newer FastDM package." : "Try Update yt-dlp in Preferences → Advanced."),
                YtDlpFailureKind.NetworkOrAccess => ("The site could not be reached or denied the request.", "Check your connection and proxy settings. The site may also restrict this request."),
                YtDlpFailureKind.Cancelled => ("The video operation was cancelled.", "No completed download was added."),
                _ => ("yt-dlp could not read this video.", packaged ? "Check the diagnostic details and update FastDM if a newer package is available." : "Check the diagnostic details and try Update yt-dlp in Preferences → Advanced.")
            };
        }
    }
}
