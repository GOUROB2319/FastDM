using System;
using System.Collections.Generic;

namespace FastDM
{
    public static class YouTubeBulkPolicy
    {
        public const int DefaultConcurrency = 1;
        public const int DefaultDelaySeconds = 3;
        public const string WaitingStatus = "Waiting before the next YouTube request";

        public static bool IsYouTubeUrl(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
            string host = uri.DnsSafeHost.TrimEnd('.');
            return host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);
        }

        public static int ClampConcurrency(int value) => Math.Clamp(value, 1, 2);
        public static int ClampDelaySeconds(int value) => Math.Clamp(value, 0, 30);

        public static int BackoffSeconds(YtDlpFailureKind kind, int priorFailures)
        {
            if (kind is not (YtDlpFailureKind.RateLimited or YtDlpFailureKind.BotVerification)) return 0;
            return priorFailures switch { <= 0 => 30, 1 => 60, _ => 120 };
        }

        public static IReadOnlyList<string> SleepArguments(string? url, bool isPlaylistOrBulkItem) =>
            isPlaylistOrBulkItem && IsYouTubeUrl(url)
                ? new[] { "--sleep-requests", "1", "--sleep-interval", "3", "--max-sleep-interval", "8" }
                : Array.Empty<string>();

        public static string ShortReason(YtDlpFailureKind kind) => kind switch
        {
            YtDlpFailureKind.BrowserDatabaseNotFound or YtDlpFailureKind.BrowserDatabaseLocked or YtDlpFailureKind.BrowserDatabasePermissionDenied or YtDlpFailureKind.MissingCookieFile or YtDlpFailureKind.InvalidCookieFile => "Cookie database",
            YtDlpFailureKind.LoginRequired or YtDlpFailureKind.CookiesRejected => "Sign-in required",
            YtDlpFailureKind.BotVerification or YtDlpFailureKind.MissingVisitorData or YtDlpFailureKind.PoTokenRequired => "Sign-in/bot check",
            YtDlpFailureKind.RateLimited => "Rate limited",
            YtDlpFailureKind.UnsupportedVideo => "Private or unavailable",
            YtDlpFailureKind.GeoRestricted => "Geo-blocked",
            YtDlpFailureKind.FormatUnavailable => "Format unavailable",
            YtDlpFailureKind.DnsConnection or YtDlpFailureKind.TemporaryNetwork or YtDlpFailureKind.NetworkOrAccess => "Network error",
            _ => "Download failed"
        };
    }
}
