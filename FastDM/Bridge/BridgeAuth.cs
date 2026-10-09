using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FastDM
{
    // টোকেন তৈরি ও যাচাই। অ্যাপে শুধু টোকেনের SHA-256 হ্যাশ থাকে, আসল টোকেন থাকে শুধু এক্সটেনশনের কাছে।
    public enum BridgeTokenState { None, Valid, WrongOrigin }

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

        // Each token is bound to the extension origin (chrome-extension://<id>) the user approved when pairing.
        // The origin list lives in s.BridgeTokenOrigins (token hash -> origin), guarded by the same lock as BridgeTokens.
        public static void AddToken(AppSettings s, string token, string? origin = null)
        {
            string o = NormalizeOrigin(origin);
            lock (s.BridgeTokens)
            {
                s.BridgeTokenOrigins ??= new Dictionary<string, string>();

                // A browser that pairs again replaces its own old token instead of piling up.
                if (o.Length > 0)
                {
                    foreach (var old in s.BridgeTokenOrigins.Where(kv => kv.Value == o).Select(kv => kv.Key).ToList())
                    {
                        s.BridgeTokenOrigins.Remove(old);
                        s.BridgeTokens.Remove(old);
                    }
                }

                string h = Hash(token);
                s.BridgeTokens.Add(h);
                if (o.Length > 0) s.BridgeTokenOrigins[h] = o;

                while (s.BridgeTokens.Count > MaxPairedClients)
                {
                    s.BridgeTokenOrigins.Remove(s.BridgeTokens[0]);
                    s.BridgeTokens.RemoveAt(0);
                }
            }
        }

        // "Forget paired browsers": drops every token and every remembered extension origin.
        public static void ForgetAll(AppSettings s)
        {
            lock (s.BridgeTokens)
            {
                s.BridgeTokens.Clear();
                s.BridgeTokenOrigins?.Clear();
            }
        }

        static bool TryMatch(AppSettings s, string? token, out string? matchedHash)
        {
            matchedHash = null;
            if (string.IsNullOrEmpty(token) || token.Length > 200) return false;
            byte[] given = SHA256.HashData(Encoding.UTF8.GetBytes(token));

            string[] stored;
            lock (s.BridgeTokens) stored = s.BridgeTokens.ToArray();

            foreach (var hex in stored)
            {
                try
                {
                    byte[] b = Convert.FromHexString(hex);
                    if (CryptographicOperations.FixedTimeEquals(b, given)) matchedHash = hex;   // সব কটা দেখি (টাইমিং ফাঁস এড়াতে)
                }
                catch { }
            }
            return matchedHash != null;
        }

        public static bool IsValid(AppSettings s, string? token) => TryMatch(s, token, out _);

        // Token check plus origin binding.
        //   Valid       token is known and was paired from this same extension origin
        //   WrongOrigin token is known but belongs to a different extension
        //   None        no/unknown token
        // A token paired before origins were remembered (older app version) is accepted once and
        // then bound to the origin it was first used from; `learned` tells the caller to save settings.
        public static BridgeTokenState Evaluate(AppSettings s, string? token, string? origin, out bool learned)
        {
            learned = false;
            if (!TryMatch(s, token, out string? hash) || hash == null) return BridgeTokenState.None;

            string o = NormalizeOrigin(origin);
            if (o.Length == 0) return BridgeTokenState.WrongOrigin;

            lock (s.BridgeTokens)
            {
                s.BridgeTokenOrigins ??= new Dictionary<string, string>();

                if (!s.BridgeTokenOrigins.TryGetValue(hash, out var bound) || string.IsNullOrEmpty(bound))
                {
                    s.BridgeTokenOrigins[hash] = o;
                    learned = true;
                    return BridgeTokenState.Valid;
                }
                return string.Equals(bound, o, StringComparison.Ordinal) ? BridgeTokenState.Valid : BridgeTokenState.WrongOrigin;
            }
        }

        // Browser extension origins only: chrome-extension://<id> (Chrome and Edge) or moz-extension://<uuid>.
        // Anything with a path, port, space or newline is rejected.
        static readonly Regex ExtensionOriginRx = new Regex(
            @"^(chrome|moz)-extension://[a-z0-9-]{8,64}\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsExtensionOrigin(string? origin) =>
            !string.IsNullOrEmpty(origin) && ExtensionOriginRx.IsMatch(origin);

        public static string NormalizeOrigin(string? origin) => (origin ?? "").Trim().ToLowerInvariant();

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

        public static string? CleanReferer(string? referer)
        {
            if (string.IsNullOrWhiteSpace(referer) || referer.Length > 2048 || HasControlChars(referer)) return null;
            if (!Uri.TryCreate(referer.Trim(), UriKind.Absolute, out var u)) return null;
            return u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps ? u.AbsoluteUri : null;
        }

        public static string? CleanUserAgent(string? ua)
        {
            if (string.IsNullOrWhiteSpace(ua) || ua.Length > 512 || HasControlChars(ua)) return null;
            return ua.Trim();
        }

        // কুকির ডোমেইন অবশ্যই ডাউনলোড-লিঙ্কের হোস্টের সাথে মিলতে হবে; না মিললে বাদ।
        // শুধু http/https লিঙ্কে কুকি চলে (ftp/sftp-এ অর্থহীন)।
        public static List<BridgeCookie>? CleanCookies(List<BridgeCookie>? cookies, string url)
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
