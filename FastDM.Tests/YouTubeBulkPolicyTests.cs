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

            var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
            T.Check("single YouTube videos and other sites bypass bulk queue pacing",
                YouTubeBulkPolicy.CanStartBulkItem(false, 4, 1, now, now.AddMinutes(1), now, 30));
            T.Check("bulk YouTube queue respects configured concurrency",
                !YouTubeBulkPolicy.CanStartBulkItem(true, 1, 1, now, DateTime.MinValue, DateTime.MinValue, 0) &&
                YouTubeBulkPolicy.CanStartBulkItem(true, 1, 2, now, DateTime.MinValue, DateTime.MinValue, 0));
            T.Check("bulk YouTube queue waits for backoff and inter-item delay",
                !YouTubeBulkPolicy.CanStartBulkItem(true, 0, 1, now, now.AddSeconds(1), DateTime.MinValue, 0) &&
                !YouTubeBulkPolicy.CanStartBulkItem(true, 0, 1, now, DateTime.MinValue, now.AddSeconds(-2), 3) &&
                YouTubeBulkPolicy.CanStartBulkItem(true, 0, 1, now, DateTime.MinValue, now.AddSeconds(-3), 3));
        }
    }
}
