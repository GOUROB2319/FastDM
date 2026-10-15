using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FastDM
{
    public static class YtDlpProxy
    {
        public static List<string> BuildArguments(ProxyMode mode, ProxyKind kind, string? host, int port, string? user)
        {
            if (mode == ProxyMode.None) return new List<string> { "--proxy", string.Empty };
            if (mode != ProxyMode.Manual || string.IsNullOrWhiteSpace(host)) return new List<string>();
            if (!string.IsNullOrEmpty(user)) return new List<string>();
            return new List<string> { "--proxy", BuildUri(kind, host, port, null, null) };
        }

        public static void ApplyEnvironment(ProcessStartInfo startInfo, ProxyMode mode, ProxyKind kind,
            string? host, int port, string? user, string? password)
        {
            if (mode != ProxyMode.Manual || string.IsNullOrWhiteSpace(host) || string.IsNullOrEmpty(user)) return;
            string proxy = BuildUri(kind, host, port, user, password ?? string.Empty);
            foreach (string key in new[] { "http_proxy", "https_proxy", "all_proxy", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" })
                startInfo.Environment[key] = proxy;
        }

        static string BuildUri(ProxyKind kind, string host, int port, string? user, string? password)
        {
            string cleanHost = host.Trim();
            if (port is < 1 or > 65535 || Uri.CheckHostName(cleanHost) == UriHostNameType.Unknown ||
                cleanHost.Contains('/') || cleanHost.Contains('@') || cleanHost.Contains('?') || cleanHost.Contains('#'))
                throw new ArgumentException("The proxy host or port is invalid.");

            var builder = new UriBuilder(kind == ProxyKind.Socks5 ? "socks5" : "http", cleanHost, port);
            if (user != null)
            {
                builder.UserName = user;
                builder.Password = password ?? string.Empty;
            }
            return builder.Uri.AbsoluteUri;
        }
    }
}
