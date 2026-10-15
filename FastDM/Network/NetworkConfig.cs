using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace FastDM
{
    public enum ProxyMode { None, System, Manual }

    public enum ProxyKind { Http, Socks5 }

    // ====================== স্পিড লিমিটার ======================
    // সব কানেকশন/ফাইল মিলিয়ে একটাই গ্লোবাল সীমা। প্রতিটা রিড করা চাঙ্কের জন্য সময়ের স্লট
    // বুক করা হয়, তাই গড় স্পিড সীমার মধ্যে থাকে আর কানেকশনগুলো সমানভাবে ভাগ পায়।
    public static class SpeedLimiter
    {
        static long bytesPerSec;           // 0 = আনলিমিটেড
        static long next;                  // চ্যানেল কখন খালি হবে (Stopwatch টাইমস্ট্যাম্প)
        static readonly object lk = new object();

        public static long BytesPerSec => Interlocked.Read(ref bytesPerSec);

        public static void SetKBps(int kbps)
        {
            Interlocked.Exchange(ref bytesPerSec, Math.Max(0, kbps) * 1024L);
            lock (lk) next = 0;            // সীমা বদলালে পুরোনো বুকিং বাতিল
        }

        public static async Task ThrottleAsync(int bytes, CancellationToken ct)
        {
            long limit = Interlocked.Read(ref bytesPerSec);
            if (limit <= 0 || bytes <= 0) return;

            long delayTicks;
            lock (lk)
            {
                long now = Stopwatch.GetTimestamp();
                if (next < now) next = now;
                next += (long)(bytes * (double)Stopwatch.Frequency / limit);
                delayTicks = next - now;
            }

            double sec = (double)delayTicks / Stopwatch.Frequency;
            if (sec > 0.005)
                await Task.Delay(TimeSpan.FromSeconds(sec), ct).ConfigureAwait(false);
        }

        public static string Describe(int kbps)
        {
            if (kbps <= 0) return "Unlimited";
            if (kbps >= 1024 && kbps % 1024 == 0) return (kbps / 1024) + " MB/s";
            return kbps + " KB/s";
        }
    }

    // ====================== প্রক্সি কনফিগ ======================
    public static class ProxyConfig
    {
        public static volatile ProxyMode Mode = ProxyMode.System;
        public static volatile ProxyKind Kind = ProxyKind.Http;
        public static string Host = "";
        public static int Port = 8080;
        public static string User = "";
        public static string Pass = "";

        public static void Load(AppSettings s)
        {
            Mode = s.Proxy;
            Kind = s.ProxyType;
            Host = (s.ProxyHost ?? "").Trim();
            Port = s.ProxyPort;
            User = s.ProxyUser ?? "";
            Pass = SecretStore.Read("proxy", out _, out var p) ? p ?? "" : "";
        }

        // ডায়ালগের মান দিয়ে সংযোগ টেস্ট (সেভ করার আগেই)
        public static async Task<string> TestAsync(ProxyMode mode, ProxyKind kind, string host, int port,
                                                   string user, string pass, CancellationToken ct)
        {
            var handler = new HttpClientHandler();
            switch (mode)
            {
                case ProxyMode.None:
                    handler.UseProxy = false;
                    break;
                case ProxyMode.System:
                    handler.UseProxy = true;
                    handler.Proxy = HttpClient.DefaultProxy;
                    break;
                default:
                    if (string.IsNullOrWhiteSpace(host))
                        throw new InvalidOperationException("Enter the proxy host first.");
                    var wp = new WebProxy((kind == ProxyKind.Socks5 ? "socks5" : "http") + "://" + host.Trim() + ":" + port);
                    if (!string.IsNullOrEmpty(user)) wp.Credentials = new NetworkCredential(user, pass ?? "");
                    handler.UseProxy = true;
                    handler.Proxy = wp;
                    break;
            }

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            var sw = Stopwatch.StartNew();
            using var resp = await client.GetAsync("http://www.msftconnecttest.com/connecttest.txt", ct);
            resp.EnsureSuccessStatusCode();
            sw.Stop();
            return "Connected (" + sw.ElapsedMilliseconds + " ms)";
        }
    }

    // HttpClient প্রতিটা রিকোয়েস্টে এখান থেকে প্রক্সি জেনে নেয়, তাই সেটিং বদলালে নতুন কানেকশনে সাথে সাথে কাজ করে
    public sealed class DynamicProxy : IWebProxy
    {
        public static readonly DynamicProxy Instance = new DynamicProxy();

        public ICredentials? Credentials
        {
            get
            {
                if (ProxyConfig.Mode == ProxyMode.System) return HttpClient.DefaultProxy.Credentials;
                if (ProxyConfig.Mode == ProxyMode.Manual && ProxyConfig.User.Length > 0)
                    return new NetworkCredential(ProxyConfig.User, ProxyConfig.Pass);
                return null;
            }
            set { }
        }

        public bool IsBypassed(Uri host)
        {
            switch (ProxyConfig.Mode)
            {
                case ProxyMode.None: return true;
                case ProxyMode.System: return HttpClient.DefaultProxy.IsBypassed(host);
                default: return string.IsNullOrWhiteSpace(ProxyConfig.Host) || host.IsLoopback;
            }
        }

        public Uri? GetProxy(Uri destination)
        {
            switch (ProxyConfig.Mode)
            {
                case ProxyMode.None:
                    return null;
                case ProxyMode.System:
                    return HttpClient.DefaultProxy.GetProxy(destination);
                default:
                    if (string.IsNullOrWhiteSpace(ProxyConfig.Host)) return null;
                    string scheme = ProxyConfig.Kind == ProxyKind.Socks5 ? "socks5" : "http";
                    return new Uri(scheme + "://" + ProxyConfig.Host + ":" + ProxyConfig.Port);
            }
        }
    }

    // ====================== সেটিং প্রয়োগ ======================
    public static class NetworkApply
    {
        // অ্যাপ চালুর সময় (HttpClient আবার বানানো লাগে না)
        public static void Load(AppSettings s)
        {
            SpeedLimiter.SetKBps(s.SpeedLimitKBps);
            ProxyConfig.Load(s);
        }

        // সেটিং বদলের পর: নতুন HttpClient, যাতে পুরোনো প্রক্সির কানেকশন আর ব্যবহার না হয়
        public static void ApplyAndReset(AppSettings s)
        {
            Load(s);
            DownloadEngine.ResetClient();
        }
    }

    // SFTP: ম্যানুয়াল প্রক্সি (HTTP / SOCKS5) থাকলে সেটা দিয়ে কানেক্ট করবে।
    // (SSH.NET সিস্টেম প্রক্সি নিজে থেকে ব্যবহার করে না)
    public static class NetworkHelper
    {
        public static SftpClient CreateSftp(string host, int port, string user, string pass)
        {
            SftpClient client;
            if (ProxyConfig.Mode == ProxyMode.Manual && ProxyConfig.Host.Length > 0)
            {
                var type = ProxyConfig.Kind == ProxyKind.Socks5 ? ProxyTypes.Socks5 : ProxyTypes.Http;
                bool hasAuth = ProxyConfig.User.Length > 0;
                var info = new ConnectionInfo(host, port, user, type,
                    ProxyConfig.Host, ProxyConfig.Port,
                    hasAuth ? ProxyConfig.User : null,
                    hasAuth ? ProxyConfig.Pass : null,
                    new PasswordAuthenticationMethod(user, pass));
                client = new SftpClient(info);
            }
            else
            {
                client = new SftpClient(host, port, user, pass);
            }

            // Every SFTP connection goes through here, so the server's identity is checked in one place.
            client.HostKeyReceived += (s, e) => e.CanTrust = SshHostKeys.Verify(host, port, e.HostKeyName, e.HostKey);
            return client;
        }
    }
}
