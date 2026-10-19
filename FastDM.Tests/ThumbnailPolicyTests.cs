using System.Net;

namespace FastDM.Tests;

static class ThumbnailPolicyTests
{
    public static async Task RunAsync()
    {
        T.Section("Video thumbnail URL and redirect policy");
        T.Check("HTTPS thumbnails from arbitrary extractor CDNs are allowed by URL policy",
            FastDM.YtThumbnailPolicy.TryParseHttpsUri("https://i0.hdslb.com/bfs/archive/test.jpg", out _, out _));
        T.Check("plain HTTPS image CDN is not restricted to YouTube hosts",
            FastDM.YtThumbnailPolicy.TryParseHttpsUri("https://cdn.example.test/preview.png", out _, out _));
        T.Check("HTTP thumbnail URLs are blocked", !FastDM.YtThumbnailPolicy.TryParseHttpsUri("http://cdn.example.test/a.jpg", out _, out _));
        T.Check("thumbnail URL userinfo is blocked", !FastDM.YtThumbnailPolicy.TryParseHttpsUri("https://user:pass@cdn.example.test/a.jpg", out _, out _));
        T.Check("invalid thumbnail URLs are blocked", !FastDM.YtThumbnailPolicy.TryParseHttpsUri("not a URL", out _, out _));
        T.Check("thumbnail response size stays capped at 2 MB", FastDM.YtThumbnailPolicy.MaxImageBytes == 2 * 1024 * 1024);
        T.Check("thumbnail timeout stays 15 seconds with one retry", FastDM.YtThumbnailPolicy.Timeout == TimeSpan.FromSeconds(15) && FastDM.YtThumbnailPolicy.MaxFetchAttempts == 2);
        T.Check("thumbnail dimensions retain their existing limits",
            FastDM.YtThumbnailPolicy.IsSafeImageDimensions(4096, 3906) &&
            !FastDM.YtThumbnailPolicy.IsSafeImageDimensions(4097, 1) &&
            !FastDM.YtThumbnailPolicy.IsSafeImageDimensions(5000, 5000) &&
            !FastDM.YtThumbnailPolicy.IsSafeImageDimensions(0, 1));

        T.Check("public IPv4 is allowed", FastDM.YtThumbnailPolicy.IsPublicAddress(IPAddress.Parse("8.8.8.8")));
        T.Check("loopback IPv4 is blocked", !FastDM.YtThumbnailPolicy.IsPublicAddress(IPAddress.Parse("127.0.0.1")));
        T.Check("RFC1918 IPv4 is blocked", !FastDM.YtThumbnailPolicy.IsPublicAddress(IPAddress.Parse("10.10.0.1")));
        T.Check("link-local IPv4 is blocked", !FastDM.YtThumbnailPolicy.IsPublicAddress(IPAddress.Parse("169.254.10.2")));
        T.Check("IPv4-mapped loopback IPv6 is blocked", !FastDM.YtThumbnailPolicy.IsPublicAddress(IPAddress.Parse("::ffff:127.0.0.1")));
        T.Check("unique-local IPv6 is blocked", !FastDM.YtThumbnailPolicy.IsPublicAddress(IPAddress.Parse("fd00::1")));
        var mixed = await FastDM.YtThumbnailPolicy.CheckHostAsync(new Uri("https://cdn.example.test/a.jpg"),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("192.168.1.1") }), CancellationToken.None);
        T.Check("DNS answers containing a private address are rejected", !mixed.Allowed && mixed.Reason == "private_or_reserved_address");

        var origin = new Uri("https://cdn.example.test/path/a.jpg");
        T.Check("relative HTTPS redirect resolves against the current host",
            FastDM.YtThumbnailPolicy.TryResolveRedirect(origin, "../b.png", 0, out var next, out _) && next.AbsoluteUri == "https://cdn.example.test/b.png");
        T.Check("redirect to HTTP is blocked", !FastDM.YtThumbnailPolicy.TryResolveRedirect(origin, "http://127.0.0.1/a.jpg", 0, out _, out _));
        T.Check("redirect userinfo is blocked", !FastDM.YtThumbnailPolicy.TryResolveRedirect(origin, "https://user@cdn.example.test/a.jpg", 0, out _, out _));
        T.Check("three redirects are allowed", FastDM.YtThumbnailPolicy.TryResolveRedirect(origin, "/one.jpg", 2, out _, out _));
        T.Check("a fourth redirect is blocked", !FastDM.YtThumbnailPolicy.TryResolveRedirect(origin, "/four.jpg", 3, out _, out _));
    }
}
