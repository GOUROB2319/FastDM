using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace FastDM
{
    // ====================== লিঙ্ক ধরন চেনা + ওয়েবপেজ থেকে ভিডিও খোঁজা ======================
    public static class MediaDetector
    {
        const int MaxCandidates = 500;

        static readonly HashSet<string> DirectExt = new HashSet<string>
        { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".flv", ".wmv", ".m4v", ".ts" };

        // এগুলোতে নেটওয়ার্ক চেক ছাড়াই সাধারণ ফাইল ধরা হবে (ডাউনলোড যাতে ধীর না হয়)
        static readonly HashSet<string> SkipExt = new HashSet<string>
        {
            ".zip", ".rar", ".7z", ".iso", ".exe", ".msi", ".apk", ".dmg", ".pdf", ".doc", ".docx", ".xls", ".xlsx",
            ".ppt", ".pptx", ".mp3", ".flac", ".wav", ".aac", ".ogg", ".jpg", ".jpeg", ".png", ".gif", ".webp",
            ".torrent", ".bin", ".gz", ".tar", ".txt", ".csv", ".json"
        };

        static readonly Regex UrlRx = new Regex(
            @"(?:https?:)?//[^\s""'<>\\()]+?\.(?:m3u8|mpd|mp4|webm|mkv)(?:\?[^\s""'<>\\()]*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static MediaKind KindFromUrl(string url)
        {
            string path;
            try { path = new Uri(url).AbsolutePath.ToLowerInvariant(); } catch { return MediaKind.None; }
            if (path.EndsWith(".m3u8")) return MediaKind.Hls;
            if (path.EndsWith(".mpd")) return MediaKind.Dash;
            if (DirectExt.Contains(Path.GetExtension(path))) return MediaKind.Direct;
            return MediaKind.None;
        }

        public static async Task<MediaProbeResult> ProbeAsync(Uri uri, CancellationToken ct,
                                                              IReadOnlyCollection<BridgeCookie>? cookies = null)
        {
            var result = new MediaProbeResult
            {
                Url = uri.AbsoluteUri,
                Kind = MediaKind.None,
                Cookies = cookies?.ToList()
            };

            var byExt = KindFromUrl(uri.AbsoluteUri);
            if (byExt == MediaKind.Hls || byExt == MediaKind.Dash) { result.Kind = byExt; return result; }
            if (byExt == MediaKind.Direct) return result;                        // সাধারণ ফাইল ইঞ্জিন সামলাবে

            string ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (SkipExt.Contains(ext)) return result;

            using var client = DownloadEngine.CreateCookieClient(uri.AbsoluteUri, cookies);
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            DownloadEngine.ApplyAuth(req);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return result;

            string ctype = (resp.Content.Headers.ContentType?.MediaType ?? "").ToLowerInvariant();
            if (ctype.Contains("mpegurl")) { result.Kind = MediaKind.Hls; return result; }
            if (ctype.Contains("dash+xml")) { result.Kind = MediaKind.Dash; return result; }
            if (!(ctype.Contains("html"))) return result;

            // HTML পেজ: ২ MB পর্যন্ত পড়ে ভিডিও লিঙ্ক খোঁজা
            string html;
            using (var st = await resp.Content.ReadAsStreamAsync(ct))
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = await st.ReadAsync(buf.AsMemory(), ct)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > 2_000_000) break;
                }
                html = Encoding.UTF8.GetString(ms.ToArray());
            }

            Sniff(html, uri, result);
            if (result.Candidates.Count > 0) result.Kind = MediaKind.Page;
            return result;
        }

        static void Sniff(string html, Uri pageUri, MediaProbeResult result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                raw = raw.Trim().Replace("&amp;", "&");
                if (raw.StartsWith("//")) raw = pageUri.Scheme + ":" + raw;
                if (!Uri.TryCreate(pageUri, raw, out var u)) return;
                if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return;

                var kind = KindFromUrl(u.AbsoluteUri);
                if (kind == MediaKind.None) return;
                if (!seen.Add(u.AbsoluteUri)) return;
                if (result.Candidates.Count >= MaxCandidates) return;

                string name = Uri.UnescapeDataString(Path.GetFileName(u.AbsolutePath));
                string tag = kind == MediaKind.Hls ? "HLS stream" : kind == MediaKind.Dash ? "DASH stream" : "Video file";
                result.Candidates.Add(new MediaCandidate
                {
                    Url = u.AbsoluteUri,
                    Kind = kind,
                    Label = tag + "  •  " + (name.Length > 0 ? name : u.Host)
                });
            }

            // ১) স্ট্রাকচার্ড ট্যাগ
            try
            {
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                string? title = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", null);
                if (string.IsNullOrWhiteSpace(title))
                    title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText;
                if (!string.IsNullOrWhiteSpace(title))
                    result.Title = HtmlEntity.DeEntitize(title).Trim();
                if (result.Title?.Length > 512) result.Title = result.Title.Substring(0, 512);

                foreach (var n in doc.DocumentNode.SelectNodes("//video[@src]|//source[@src]") ?? new HtmlNodeCollection(null))
                    Add(HtmlEntity.DeEntitize(n.GetAttributeValue("src", "")));

                foreach (var n in doc.DocumentNode.SelectNodes(
                    "//meta[@property='og:video' or @property='og:video:url' or @property='og:video:secure_url' or @name='twitter:player:stream']")
                    ?? new HtmlNodeCollection(null))
                    Add(HtmlEntity.DeEntitize(n.GetAttributeValue("content", "")));
            }
            catch (Exception exIgnored1) { AppLog.Write("MediaDetector (ignored): " + exIgnored1.Message); }

            // ২) পেজের লেখার ভেতরে (স্ক্রিপ্ট/JSON) থাকা লিঙ্ক
            string flat = html.Replace("\\/", "/").Replace("\\u0026", "&").Replace("\\u002F", "/");
            foreach (Match m in UrlRx.Matches(flat)) Add(m.Value);
        }
    }

    // ====================== নেটওয়ার্ক হেল্পার ======================
    public static class MediaNet
    {
        const int MaxTextBytes = 4_000_000;

        public static async Task<string> GetTextAsync(string url, string? referer, CancellationToken ct,
                                                       int maxBytes = 4_000_000, HttpClient? client = null)
        {
            if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            maxBytes = Math.Min(maxBytes, MaxTextBytes);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(referer))
            {
                try { req.Headers.Referrer = new Uri(referer); } catch (Exception exIgnored2) { AppLog.Write("MediaDetector (ignored): " + exIgnored2.Message); }
            }
            DownloadEngine.ApplyAuth(req);

            using var resp = await (client ?? DownloadEngine.SharedClient).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            DownloadEngine.CheckAuth(resp);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
                throw new InvalidDataException("The media manifest exceeds FastDM's 4 MB safety limit.");

            using var st = await resp.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = await st.ReadAsync(buf.AsMemory(0, Math.Min(buf.Length, maxBytes - (int)ms.Length + 1)), ct)) > 0)
            {
                if (n > maxBytes - ms.Length)
                    throw new InvalidDataException("The media manifest exceeds FastDM's 4 MB safety limit.");
                ms.Write(buf, 0, n);
            }
            return Encoding.UTF8.GetString(ms.ToArray()).TrimStart('\uFEFF');
        }
    }

    // ====================== ffmpeg খুঁজে বের করা ======================
    public static class FfmpegLocator
    {
        public static string? Find()
        {
            string[] local =
            {
                Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg.exe"),
                Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
            };
            foreach (var c in local) if (File.Exists(c)) return c;
            // Never execute an arbitrary ffmpeg found through PATH. The supported binary is
            // installed next to FastDM so packaged and portable builds use a known location.
            return null;
        }
    }
}
