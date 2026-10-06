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

        public static bool IsAllowedUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > 8192) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
            string sc = u.Scheme.ToLowerInvariant();
            return sc == "http" || sc == "https" || sc == "ftp" || sc == "sftp";
        }
    }
}
