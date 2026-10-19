using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FastDM
{
    public readonly record struct YtThumbnailCandidate(string Url, int Width);

    public static class YtThumbnailPolicy
    {
        public const int MaxImageBytes = 2 * 1024 * 1024;
        public const int MaxDimension = 4096;
        public const long MaxPixels = 16_000_000;
        public const int MaxRedirects = 3;
        public const int MaxFetchAttempts = 2;
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        public static bool IsSafeImageDimensions(int width, int height) =>
            width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension && (long)width * height <= MaxPixels;

        public static bool TryParseHttpsUri(string? value, out Uri uri, out string reason)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            {
                uri = null!;
                reason = "invalid_url";
                return false;
            }
            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                uri = null!;
                reason = "https_required";
                return false;
            }
            if (!string.IsNullOrEmpty(parsed.UserInfo))
            {
                uri = null!;
                reason = "userinfo_not_allowed";
                return false;
            }
            if (string.IsNullOrWhiteSpace(parsed.DnsSafeHost))
            {
                uri = null!;
                reason = "missing_host";
                return false;
            }
            uri = parsed;
            reason = "";
            return true;
        }

        public static async Task<(bool Allowed, string Reason)> CheckHostAsync(
            Uri uri,
            Func<string, CancellationToken, Task<IPAddress[]>> resolve,
            CancellationToken cancellationToken)
        {
            string host = uri.DnsSafeHost;
            if (IPAddress.TryParse(host, out var literal))
                return IsPublicAddress(literal) ? (true, "") : (false, "private_or_reserved_address");

            IPAddress[] addresses;
            try { addresses = await resolve(host, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { return (false, "dns_resolution_failed"); }
            if (addresses.Length == 0) return (false, "dns_no_addresses");
            if (addresses.Any(address => !IsPublicAddress(address)))
                return (false, "private_or_reserved_address");
            return (true, "");
        }

        public static bool IsPublicAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                return IsPublicAddress(address.MapToIPv4());

            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte a = bytes[0], b = bytes[1], c = bytes[2];
                if (a == 0 || a == 10 || a == 127 || a >= 224) return false;
                if (a == 100 && b >= 64 && b <= 127) return false;
                if (a == 169 && b == 254) return false;
                if (a == 172 && b >= 16 && b <= 31) return false;
                if (a == 192 && (b == 0 || b == 168) || a == 192 && b == 88 && c == 99) return false;
                if (a == 198 && (b == 18 || b == 19 || b == 51 && c == 100)) return false;
                if (a == 203 && b == 0 && c == 113) return false;
                return !(a == 255 && b == 255 && c == 255 && bytes[3] == 255);
            }

            if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
            if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6LinkLocal ||
                address.IsIPv6Multicast || address.IsIPv6SiteLocal || address.IsIPv6Teredo)
                return false;
            if ((bytes[0] & 0xFE) == 0xFC) return false; // Unique-local fc00::/7.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return false; // Documentation.
            if (bytes[0] == 0x20 && bytes[1] == 0x02) return false; // 6to4 can embed a private IPv4 endpoint.
            if (bytes.Take(12).All(value => value == 0)) return false; // IPv4-compatible or unspecified range.
            return true;
        }

        public static bool TryResolveRedirect(Uri current, string? location, int followed,
            out Uri next, out string reason)
        {
            if (followed >= MaxRedirects)
            {
                next = null!;
                reason = "redirect_limit";
                return false;
            }
            if (string.IsNullOrWhiteSpace(location) || !Uri.TryCreate(current, location, out var resolved))
            {
                next = null!;
                reason = "invalid_redirect_location";
                return false;
            }
            if (!TryParseHttpsUri(resolved.AbsoluteUri, out next, out reason)) return false;
            return true;
        }

        public static string SelectBestCandidate(IEnumerable<YtThumbnailCandidate> candidates, bool isYouTube) =>
            SelectBestCandidate(candidates, isYouTube, out _);

        public static string SelectBestCandidate(IEnumerable<YtThumbnailCandidate> candidates, bool isYouTube, out bool needsConversion)
        {
            var valid = candidates
                .Where(candidate => Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
                .ToList();
            var nonWebp = valid.Where(candidate => !IsWebp(candidate.Url))
                .OrderByDescending(candidate => candidate.Width).FirstOrDefault();
            string selected = nonWebp.Url;
            needsConversion = string.IsNullOrEmpty(selected);
            if (needsConversion)
                selected = valid.OrderByDescending(candidate => candidate.Width).FirstOrDefault().Url;
            needsConversion = needsConversion && IsWebp(selected);
            return string.IsNullOrEmpty(selected) ? "" : isYouTube ? MapYouTubeWebpToJpeg(selected) : selected;
        }

        public static bool IsWebp(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.AbsolutePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

        public static string MapYouTubeWebpToJpeg(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsWebp(value)) return value;
            string host = uri.IdnHost;
            if (!host.Equals("ytimg.com", StringComparison.OrdinalIgnoreCase) &&
                !host.EndsWith(".ytimg.com", StringComparison.OrdinalIgnoreCase)) return value;
            string path = uri.AbsolutePath;
            if (!path.StartsWith("/vi_webp/", StringComparison.OrdinalIgnoreCase)) return value;
            var builder = new UriBuilder(uri)
            {
                Path = "/vi/" + path.Substring("/vi_webp/".Length),
                Query = uri.Query.TrimStart('?')
            };
            builder.Path = builder.Path.Substring(0, builder.Path.Length - ".webp".Length) + ".jpg";
            return builder.Uri.AbsoluteUri;
        }
    }
}
