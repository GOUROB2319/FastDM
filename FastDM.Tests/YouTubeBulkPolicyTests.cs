using FastDM;

namespace FastDM.Tests
{
    static class YouTubeBulkPolicyTests
    {
        public static void Run()
        {
            T.Section("YouTube bulk policy");
            T.Check("YouTube and YouTube Music hosts use the bulk policy", YouTubeBulkPolicy.IsYouTubeUrl("https://music.youtube.com/watch?v=abc") && YouTubeBulkPolicy.IsYouTubeUrl("https://youtu.be/abc"));
            T.Check("other video sites retain their normal capacity", !YouTubeBulkPolicy.IsYouTubeUrl("https://www.bilibili.com/video/BV1"));
            T.Check("YouTube concurrency stays between one and two", YouTubeBulkPolicy.ClampConcurrency(0) == 1 && YouTubeBulkPolicy.ClampConcurrency(7) == 2);
            T.Check("playlist work gets yt-dlp pacing while single videos do not", YouTubeBulkPolicy.SleepArguments("https://youtube.com/watch?v=abc", true).Count == 6 && YouTubeBulkPolicy.SleepArguments("https://youtube.com/watch?v=abc", false).Count == 0);
            T.Check("rate and bot failures use 30, 60, then 120 second backoff", YouTubeBulkPolicy.BackoffSeconds(YtDlpFailureKind.RateLimited, 0) == 30 && YouTubeBulkPolicy.BackoffSeconds(YtDlpFailureKind.BotVerification, 1) == 60 && YouTubeBulkPolicy.BackoffSeconds(YtDlpFailureKind.RateLimited, 2) == 120);
            T.Check("playlist reasons stay short and actionable", YouTubeBulkPolicy.ShortReason(YtDlpFailureKind.GeoRestricted) == "Geo-blocked" && YouTubeBulkPolicy.ShortReason(YtDlpFailureKind.FormatUnavailable) == "Format unavailable");
        }
    }
}
