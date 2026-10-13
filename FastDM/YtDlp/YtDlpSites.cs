#nullable enable
using System;

namespace FastDM
{
    // Sites that yt-dlp handles. A link on one of these hosts always goes to yt-dlp,
    // never to the folder scanner or the plain file downloader.
    public static class YtDlpSites
    {
        static readonly string[] Hosts =
        {
            "youtube.com", "youtu.be", "youtube-nocookie.com", "tiktok.com", "facebook.com", "fb.watch",
            "instagram.com", "twitter.com", "x.com", "vimeo.com", "dailymotion.com", "dai.ly", "reddit.com",
            "twitch.tv", "soundcloud.com", "bilibili.com", "b23.tv", "streamable.com", "rumble.com",
            "odysee.com", "ok.ru", "vk.com", "mixcloud.com", "bandcamp.com", "pinterest.com", "tumblr.com"
        };

        public static bool IsSupported(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
            string host = u.Host.ToLowerInvariant();
            foreach (var h in Hosts)
                if (host == h || host.EndsWith("." + h, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
