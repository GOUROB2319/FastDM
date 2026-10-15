using FastDM;

namespace FastDM.Tests
{
    static class CookieScopeTests
    {
        public static void Run()
        {
            T.Section("Request-scoped browser cookies");

            var a = DownloadCookieScope.Create("https://www.example.test/a/file", new[]
            {
                new BridgeCookie { Name = "session", Value = "A", Domain = "www.example.test", Path = "/" }
            });
            var b = DownloadCookieScope.Create("https://www.example.test/b/file", new[]
            {
                new BridgeCookie { Name = "session", Value = "B", Domain = "www.example.test", Path = "/" }
            });
            var sibling = new Uri("https://api.example.test/file");
            var host = new Uri("https://www.example.test/file");

            T.Check("same-host downloads receive only their own cookie jar", a.GetCookieHeader(host) == "session=A" && b.GetCookieHeader(host) == "session=B");
            T.Check("host-only cookies do not reach sibling subdomains", a.GetCookieHeader(sibling).Length == 0);

            var wide = DownloadCookieScope.Create("https://www.example.test/a/file", new[]
            {
                new BridgeCookie { Name = "domain", Value = "D", Domain = ".example.test", Path = "/" }
            });
            T.Check("domain cookies retain their intended subdomain scope", wide.GetCookieHeader(sibling) == "domain=D");

            var scoped = DownloadCookieScope.Create("https://www.example.test/private/file", new[]
            {
                new BridgeCookie { Name = "path", Value = "P", Domain = "www.example.test", Path = "/private" },
                new BridgeCookie { Name = "secure", Value = "S", Domain = "www.example.test", Path = "/", Secure = true }
            });
            T.Check("path and secure flags are enforced by CookieContainer", scoped.GetCookieHeader(new Uri("https://www.example.test/public")) == "secure=S" && scoped.GetCookieHeader(new Uri("http://www.example.test/private/file")) == "path=P");

            var foreign = DownloadCookieScope.Create("https://www.example.test/file", new[]
            {
                new BridgeCookie { Name = "foreign", Value = "X", Domain = "evil.test", Path = "/" }
            });
            T.Check("foreign-domain cookies are rejected", foreign.GetCookieHeader(host).Length == 0);

            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            var expiring = DownloadCookieScope.Create("https://www.example.test/file", new[]
            {
                new BridgeCookie { Name = "live", Value = "L", Domain = "www.example.test", Path = "/", ExpirationDate = now + 60 },
                new BridgeCookie { Name = "stale", Value = "S", Domain = "www.example.test", Path = "/", ExpirationDate = now - 60 }
            });
            T.Check("expired cookies are dropped and valid cookie expirations are retained", expiring.GetCookieHeader(host) == "live=L");

            var cleaned = BridgeAuth.CleanCookies(new List<BridgeCookie>
            {
                new() { Name = "live", Value = "L", Domain = "www.example.test", Path = "/", ExpirationDate = now + 60 },
                new() { Name = "stale", Value = "S", Domain = "www.example.test", Path = "/", ExpirationDate = now - 60 }
            }, "https://www.example.test/file");
            T.Check("bridge validation drops expired cookies before they reach an item", cleaned?.Count == 1 && cleaned[0].ExpirationDate > now);
        }
    }
}
