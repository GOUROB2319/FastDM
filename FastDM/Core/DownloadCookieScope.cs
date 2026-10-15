using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace FastDM
{
    // Builds a fresh cookie jar for one external-add request or download item.
    // Cookie values are intentionally kept out of AppSettings and DownloadItem JSON.
    public static class DownloadCookieScope
    {
        public const int MaxCookies = 100;

        public static CookieContainer Create(string url, IReadOnlyCollection<BridgeCookie>? cookies)
        {
            var jar = new CookieContainer();
            if (cookies == null || cookies.Count == 0 ||
                !Uri.TryCreate(url, UriKind.Absolute, out var target) ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
                return jar;

            string host = target.IdnHost.TrimEnd('.').ToLowerInvariant();
            foreach (var source in cookies.Take(MaxCookies))
            {
                if (source == null || string.IsNullOrEmpty(source.Name) || string.IsNullOrEmpty(source.Domain))
                    continue;

                string domain = source.Domain.Trim().TrimStart('.').TrimEnd('.').ToLowerInvariant();
                if (domain.Length == 0 || !(host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)))
                    continue;

                string path = string.IsNullOrEmpty(source.Path) || source.Path[0] != '/' ? "/" : source.Path;
                if (source.Name.Length > 4096 || (source.Value ?? "").Length > 4096 ||
                    HasControl(source.Name) || HasControl(source.Value ?? "") ||
                    source.Name.IndexOfAny(new[] { ';', '=', ',', ' ' }) >= 0)
                    continue;

                DateTime? expires = null;
                if (source.ExpirationDate is double expiration)
                {
                    double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
                    double latest = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() / 1000d;
                    if (!double.IsFinite(expiration) || expiration <= now || expiration > latest) continue;
                    expires = DateTimeOffset.FromUnixTimeMilliseconds((long)(expiration * 1000)).UtcDateTime;
                }

                try
                {
                    Cookie cookie;
                    if (source.Domain.TrimStart().StartsWith(".", StringComparison.Ordinal))
                    {
                        cookie = new Cookie(source.Name, source.Value ?? "", path, "." + domain);
                    }
                    else
                    {
                        // Add(Uri, Cookie) assigns a host-only domain to this target host.
                        cookie = new Cookie(source.Name, source.Value ?? "", path);
                    }
                    cookie.Secure = source.Secure;
                    if (expires.HasValue) cookie.Expires = expires.Value;
                    jar.Add(target, cookie);
                }
                catch (CookieException) { }
                catch (ArgumentException) { }
            }

            return jar;
        }

        static bool HasControl(string value) => value.Any(c => c < 0x20 || c == 0x7f);
    }
}
