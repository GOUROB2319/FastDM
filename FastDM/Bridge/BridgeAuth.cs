#nullable disable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FastDM
{
    // টোকেন তৈরি ও যাচাই। অ্যাপে শুধু টোকেনের SHA-256 হ্যাশ থাকে, আসল টোকেন থাকে শুধু এক্সটেনশনের কাছে।
    public static class BridgeAuth
    {
        public const int MaxPairedClients = 10;

        public static string NewToken()
        {
            byte[] b = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string Hash(string token)
        {
            byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""));
            return Convert.ToHexString(h);
        }

        public static void AddToken(AppSettings s, string token)
        {
            lock (s.BridgeTokens)
            {
                s.BridgeTokens.Add(Hash(token));
                while (s.BridgeTokens.Count > MaxPairedClients) s.BridgeTokens.RemoveAt(0);
            }
        }

        public static bool IsValid(AppSettings s, string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 200) return false;
            byte[] given = SHA256.HashData(Encoding.UTF8.GetBytes(token));

            string[] stored;
            lock (s.BridgeTokens) stored = s.BridgeTokens.ToArray();

            bool ok = false;
            foreach (var hex in stored)
            {
                try
                {
                    byte[] b = Convert.FromHexString(hex);
                    if (CryptographicOperations.FixedTimeEquals(b, given)) ok = true;   // সব কটা দেখি (টাইমিং ফাঁস এড়াতে)
                }
                catch { }
            }
            return ok;
        }

        public static bool IsExtensionOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return false;
            return origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase);
        }

        // ---------- Phase 2: অনুরোধের অতিরিক্ত ফিল্ড যাচাই (পেয়ার করা এক্সটেনশনকেও অন্ধভাবে বিশ্বাস করি না) ----------
        public const int MaxCookies = 100;
        const int MaxCookieField = 4096;

        public static string NormalizeMode(string mode)
        {
            if (string.Equals(mode, "download", StringComparison.OrdinalIgnoreCase)) return "download";
            if (string.Equals(mode, "playlist", StringComparison.OrdinalIgnoreCase)) return "playlist";
            return "open";
        }

        static bool HasControlChars(string v)
        {
            foreach (char c in v)
                if (c < 0x20 || c == 0x7F) return true;
            return false;
        }

        public static string CleanReferer(string referer)
        {
            if (string.IsNullOrWhiteSpace(referer) || referer.Length > 2048 || HasControlChars(referer)) return null;
            if (!Uri.TryCreate(referer.Trim(), UriKind.Absolute, out var u)) return null;
            return u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps ? u.AbsoluteUri : null;
        }

        public static string CleanUserAgent(string ua)
        {
            if (string.IsNullOrWhiteSpace(ua) || ua.Length > 512 || HasControlChars(ua)) return null;
            return ua.Trim();
        }

        // কুকির ডোমেইন অবশ্যই ডাউনলোড-লিঙ্কের হোস্টের সাথে মিলতে হবে; না মিললে বাদ।
        // শুধু http/https লিঙ্কে কুকি চলে (ftp/sftp-এ অর্থহীন)।
        public static List<BridgeCookie> CleanCookies(List<BridgeCookie> cookies, string url)
        {
            if (cookies == null || cookies.Count == 0) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return null;

            string host = u.Host.ToLowerInvariant();
            var clean = new List<BridgeCookie>();

            foreach (var c in cookies)
            {
                if (clean.Count >= MaxCookies) break;
                if (c == null || string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Domain)) continue;
                if (c.Name.Length > MaxCookieField || (c.Value ?? "").Length > MaxCookieField) continue;
                if (HasControlChars(c.Name) || HasControlChars(c.Value ?? "") || c.Name.IndexOfAny(new[] { ';', '=', ',', ' ' }) >= 0) continue;

                string d = c.Domain.Trim().TrimStart('.').ToLowerInvariant();
                if (d.Length == 0 || !(host == d || host.EndsWith("." + d, StringComparison.Ordinal))) continue;

                string path = string.IsNullOrEmpty(c.Path) || c.Path[0] != '/' || HasControlChars(c.Path) ? "/" : c.Path;
                // প্রথমের ডট রাখি: ডট থাকলে সাবডোমেইনেও যায় (ব্রাউজারের domain cookie), না থাকলে শুধু ওই হোস্টে (host-only)
                bool domainWide = c.Domain.TrimStart().StartsWith(".");
                clean.Add(new BridgeCookie { Name = c.Name, Value = c.Value ?? "", Domain = domainWide ? "." + d : d, Path = path, Secure = c.Secure });
            }
            return clean.Count > 0 ? clean : null;
        }

        public static bool IsAllowedUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > 8192) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
            string sc = u.Scheme.ToLowerInvariant();
            return sc == "http" || sc == "https" || sc == "ftp" || sc == "sftp";
        }
    }
}
