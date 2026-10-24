using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace FastDM
{
    /// <summary>
    /// Validates thumbnail URLs and ensures the socket connects only to a public IP.
    /// Thumbnail metadata comes from remote extractors, so it must not be trusted as a URL.
    /// </summary>
    public static class ThumbnailUrlPolicy
    {
        public static bool IsAllowedUrl(string? value) =>
            TryCreateAllowedUri(value, out _);

        public static bool TryCreateAllowedUri(string? value, out Uri uri)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
                parsed.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(parsed.UserInfo) ||
                parsed.Port != 443 ||
                string.IsNullOrWhiteSpace(parsed.IdnHost))
            {
                uri = null!;
                return false;
            }

            string host = parsed.IdnHost.TrimEnd('.').ToLowerInvariant();
            if (host.Length == 0 ||
                !host.Contains(".", StringComparison.Ordinal) ||
                host is "localhost" ||
                host.EndsWith(".localhost", StringComparison.Ordinal) ||
                host.EndsWith(".local", StringComparison.Ordinal) ||
                host.EndsWith(".internal", StringComparison.Ordinal) ||
                host.EndsWith(".lan", StringComparison.Ordinal) ||
                host.EndsWith(".home.arpa", StringComparison.Ordinal) ||
                host.EndsWith(".test", StringComparison.Ordinal) ||
                host.EndsWith(".invalid", StringComparison.Ordinal) ||
                host.EndsWith(".example", StringComparison.Ordinal))
            {
                uri = null!;
                return false;
            }

            if (IPAddress.TryParse(host, out var literal) && !IsPublicAddress(literal))
            {
                uri = null!;
                return false;
            }

            uri = parsed;
            return true;
        }

        /// <summary>
        /// Rejects non-global IPv4/IPv6 ranges, including loopback, private, link-local,
        /// documentation, multicast and reserved addresses.
        /// </summary>
        public static bool IsPublicAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                uint value = ((uint)bytes[0] << 24) |
                             ((uint)bytes[1] << 16) |
                             ((uint)bytes[2] << 8) |
                             bytes[3];

                return !InCidr(value, 0x00000000, 8) &&  // 0.0.0.0/8
                       !InCidr(value, 0x0A000000, 8) &&  // 10.0.0.0/8
                       !InCidr(value, 0x64400000, 10) && // carrier-grade NAT
                       !InCidr(value, 0x7F000000, 8) &&  // loopback
                       !InCidr(value, 0xA9FE0000, 16) && // link-local
                       !InCidr(value, 0xAC100000, 12) && // 172.16.0.0/12
                       !InCidr(value, 0xC0000000, 24) && // IETF special-purpose
                       !InCidr(value, 0xC0000200, 24) && // documentation
                       !InCidr(value, 0xC0586300, 24) && // deprecated 6to4 relay
                       !InCidr(value, 0xC0A80000, 16) && // 192.168.0.0/16
                       !InCidr(value, 0xC6120000, 15) && // benchmarking
                       !InCidr(value, 0xC6336400, 24) && // documentation
                       !InCidr(value, 0xCB007100, 24) && // documentation
                       !InCidr(value, 0xE0000000, 4);    // multicast/reserved
            }

            if (address.AddressFamily != AddressFamily.InterNetworkV6)
                return false;

            if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal ||
                address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return false;

            byte[] v6 = address.GetAddressBytes();
            // Only allow global-unicast 2000::/3. This excludes unspecified, loopback,
            // ULA, link-local, multicast, and most transition/special-purpose ranges.
            if ((v6[0] & 0xE0) != 0x20)
                return false;

            // 2001:db8::/32 is reserved for documentation.
            return !(v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x0D && v6[3] == 0xB8);
        }

        static bool InCidr(uint address, uint network, int prefixLength)
        {
            uint mask = uint.MaxValue << (32 - prefixLength);
            return (address & mask) == (network & mask);
        }

        /// <summary>
        /// Resolves the requested host, filters out all non-public answers, and connects
        /// directly to a validated address. TLS still uses the original request hostname.
        /// </summary>
        public static async ValueTask<Stream> ConnectToPublicAddressAsync(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            string host = context.DnsEndPoint.Host;
            int port = context.DnsEndPoint.Port;

            if (port != 443 ||
                !TryCreateAllowedUri("https://" + host + "/", out _))
                throw new HttpRequestException("Thumbnail host is not allowed.");

            IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
                ? new[] { literal }
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

            IPAddress[] publicAddresses = addresses.Where(IsPublicAddress).ToArray();
            if (publicAddresses.Length == 0)
                throw new HttpRequestException("Thumbnail host did not resolve to a public address.");

            Exception? lastError = null;
            foreach (IPAddress address in publicAddresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };

                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException ex)
                {
                    socket.Dispose();
                    lastError = ex;
                }
            }

            throw new HttpRequestException("Could not connect to the thumbnail host.", lastError);
        }
    }
}
