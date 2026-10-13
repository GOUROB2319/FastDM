namespace FastDM.Tests
{
    // Links on these sites must go to yt-dlp (never to the folder scanner), whatever they end with.
    static class YtDlpSitesTests
    {
        public static void Run()
        {
            T.Section("Links that belong to yt-dlp");

            foreach (var url in new[]
            {
                "https://www.youtube.com/watch?v=yWNPxS0sXnE&list=RDyWNPxS0sXnE",
                "https://youtu.be/yWNPxS0sXnE",
                "https://m.youtube.com/watch?v=abc",
                "https://www.facebook.com/reel/1234567890123456/",      // ends with "/": used to be taken for a folder
                "https://www.facebook.com/reel/1234567890123456",
                "https://web.facebook.com/watch/?v=123",
                "https://fb.watch/abcDEF/",
                "https://www.instagram.com/reel/Cxyz123abc/",
                "https://www.instagram.com/p/Cxyz123abc/",
                "https://www.tiktok.com/@user/video/7300000000000000000",
                "https://x.com/user/status/123",
                "HTTPS://WWW.YOUTUBE.COM/watch?v=abc",
                "  https://youtu.be/abc  ",
            })
                T.Check("yt-dlp site: " + url.Trim(), YtDlpSites.IsSupported(url));

            foreach (var url in new[]
            {
                "https://notyoutube.com/watch?v=abc",                   // look-alike hosts
                "https://youtube.com.evil.example/watch?v=abc",
                "https://example.com/youtube.com/watch?v=abc",
                "https://files.example.com/movies/",                    // a real folder link
                "https://example.com/video.mp4",
                "ftp://youtube.com/video",
                "sftp://youtube.com/video",
                "javascript:alert(1)",
                "not a link",
                "",
                "   ",
            })
                T.Check("not a yt-dlp site: '" + url.Trim() + "'", !YtDlpSites.IsSupported(url));

            T.Check("null is not a yt-dlp site", !YtDlpSites.IsSupported(null));
        }
    }
}
