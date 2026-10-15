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
    // ====================== HLS পার্সার ======================
    public static class Hls
    {
        public class Variant { public string Url = ""; public string? Codecs, AudioGroup; public long Bandwidth; public int W, H; }
        public class AudioTrack { public string Url = ""; public string? Group, Name, Lang; public bool Default; }
        public class Master { public List<Variant> Variants = new List<Variant>(); public List<AudioTrack> Audios = new List<AudioTrack>(); }

        public class Seg
        {
            public string Url = ""; public string? KeyUrl, IvHex;
            public long RangeStart = -1, RangeLen, Seq;
        }

        public class MediaPlaylist
        {
            public List<Seg> Segs = new List<Seg>();
            public Seg? Init;
            public bool Ended, Fmp4;
            public string? Unsupported;
        }

        public static bool IsMaster(string text) => text.Contains("#EXT-X-STREAM-INF");

        static Dictionary<string, string> Attrs(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(s, @"([A-Za-z0-9\-]+)=(""[^""]*""|[^,]*)"))
                d[m.Groups[1].Value] = m.Groups[2].Value.Trim('"');
            return d;
        }

        // "length@offset" বা "length"
        static void ParseRange(string s, out long len, out long off)
        {
            len = -1; off = -1;
            var p = s.Split('@');
            long.TryParse(p[0], out len);
            if (p.Length > 1) long.TryParse(p[1], out off);
        }

        public static Master ParseMaster(string text, Uri baseUri)
        {
            var m = new Master();
            var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.StartsWith("#EXT-X-STREAM-INF:"))
                {
                    var a = Attrs(l.Substring(18));
                    int j = i + 1;
                    while (j < lines.Length && (lines[j].Length == 0 || lines[j].StartsWith("#"))) j++;
                    if (j >= lines.Length) break;

                    var v = new Variant { Url = new Uri(baseUri, lines[j]).AbsoluteUri };
                    if (a.TryGetValue("BANDWIDTH", out var bw)) long.TryParse(bw, out v.Bandwidth);
                    if (a.TryGetValue("RESOLUTION", out var res))
                    {
                        var p = res.Split('x');
                        if (p.Length == 2) { int.TryParse(p[0], out v.W); int.TryParse(p[1], out v.H); }
                    }
                    a.TryGetValue("CODECS", out v.Codecs);
                    a.TryGetValue("AUDIO", out v.AudioGroup);
                    m.Variants.Add(v);
                    i = j;
                }
                else if (l.StartsWith("#EXT-X-MEDIA:"))
                {
                    var a = Attrs(l.Substring(13));
                    if (a.TryGetValue("TYPE", out var type) && type.Equals("AUDIO", StringComparison.OrdinalIgnoreCase))
                    {
                        a.TryGetValue("URI", out var uri);
                        if (string.IsNullOrEmpty(uri)) continue;     // ভিডিওর ভেতরেই অডিও আছে
                        var t = new AudioTrack { Url = new Uri(baseUri, uri).AbsoluteUri };
                        a.TryGetValue("GROUP-ID", out t.Group);
                        a.TryGetValue("NAME", out t.Name);
                        a.TryGetValue("LANGUAGE", out t.Lang);
                        t.Default = a.TryGetValue("DEFAULT", out var df) && df.Equals("YES", StringComparison.OrdinalIgnoreCase);
                        m.Audios.Add(t);
                    }
                }
            }
            return m;
        }

        public static MediaPlaylist ParseMedia(string text, Uri baseUri)
        {
            var pl = new MediaPlaylist();
            long seq = 0, lastEnd = 0, pendLen = -1, pendOff = -1;
            string? keyUrl = null, keyIv = null;

            foreach (var raw in text.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0) continue;

                if (l.StartsWith("#EXT-X-MEDIA-SEQUENCE:"))
                {
                    long.TryParse(l.Substring(22), out seq);
                }
                else if (l.StartsWith("#EXT-X-KEY:"))
                {
                    var a = Attrs(l.Substring(11));
                    a.TryGetValue("METHOD", out var method);
                    if (string.Equals(method, "NONE", StringComparison.OrdinalIgnoreCase)) { keyUrl = null; keyIv = null; }
                    else if (string.Equals(method, "AES-128", StringComparison.OrdinalIgnoreCase))
                    {
                        a.TryGetValue("URI", out var ku);
                        keyUrl = ku == null ? null : new Uri(baseUri, ku).AbsoluteUri;
                        a.TryGetValue("IV", out keyIv);
                    }
                    else pl.Unsupported = "This stream uses " + method + " encryption (DRM), which is not supported.";
                }
                else if (l.StartsWith("#EXT-X-MAP:"))
                {
                    var a = Attrs(l.Substring(11));
                    if (a.TryGetValue("URI", out var mu))
                    {
                        pl.Init = new Seg { Url = new Uri(baseUri, mu).AbsoluteUri, Seq = -1 };
                        if (a.TryGetValue("BYTERANGE", out var br))
                        {
                            ParseRange(br, out long len, out long off);
                            if (len >= 0) { pl.Init.RangeLen = len; pl.Init.RangeStart = off >= 0 ? off : 0; }
                        }
                        pl.Fmp4 = true;
                    }
                }
                else if (l.StartsWith("#EXT-X-BYTERANGE:"))
                {
                    ParseRange(l.Substring(17), out pendLen, out pendOff);
                }
                else if (l.StartsWith("#EXT-X-ENDLIST"))
                {
                    pl.Ended = true;
                }
                else if (!l.StartsWith("#"))
                {
                    var s = new Seg { Url = new Uri(baseUri, l).AbsoluteUri, KeyUrl = keyUrl, IvHex = keyIv, Seq = seq++ };
                    if (pendLen >= 0)
                    {
                        long off = pendOff >= 0 ? pendOff : lastEnd;
                        s.RangeStart = off;
                        s.RangeLen = pendLen;
                        lastEnd = off + pendLen;
                        pendLen = -1; pendOff = -1;
                    }
                    pl.Segs.Add(s);
                }
            }

            if (pl.Unsupported == null && !pl.Ended) pl.Unsupported = "Live streams are not supported yet.";
            if (pl.Unsupported == null && pl.Segs.Count == 0) pl.Unsupported = "The playlist has no segments.";

            if (!pl.Fmp4 && pl.Segs.Count > 0)
            {
                string ext = Path.GetExtension(new Uri(pl.Segs[0].Url).AbsolutePath).ToLowerInvariant();
                pl.Fmp4 = ext == ".m4s" || ext == ".mp4" || ext == ".cmfv" || ext == ".cmfa" || ext == ".m4a" || ext == ".m4v";
            }
            return pl;
        }
    }

    // ====================== DASH পার্সার ======================
    public static class Dash
    {
        public class Rep
        {
            public string Id = "", Kind = "", Mime = "", InitUrl = ""; public string? Codecs, Lang;
            public long Bandwidth;
            public int W, H;
            public List<string> SegUrls = new List<string>();
        }

        public class Mpd
        {
            public List<Rep> Reps = new List<Rep>();
            public string? Unsupported;
        }

        static IEnumerable<XElement> Els(XElement e, string name) =>
            e == null ? Enumerable.Empty<XElement>() : e.Elements().Where(x => x.Name.LocalName == name);
        static XElement? El(XElement e, string name) => Els(e, name).FirstOrDefault();
        static string? At(XElement? e, string a) => e?.Attribute(a)?.Value;

        static Uri Base(Uri cur, XElement e)
        {
            string? b = El(e, "BaseURL")?.Value?.Trim();
            return string.IsNullOrEmpty(b) ? cur : new Uri(cur, b);
        }

        static double ParseDur(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            try { return XmlConvert.ToTimeSpan(s).TotalSeconds; } catch { return 0; }
        }

        public static Mpd Parse(string xml, Uri mpdUri)
        {
            var res = new Mpd();
            var root = XDocument.Parse(xml).Root;
            if (root == null) throw new InvalidDataException("The MPD has no root element.");

            if (At(root, "type") == "dynamic") { res.Unsupported = "Live DASH streams are not supported yet."; return res; }
            if (root.Descendants().Any(x => x.Name.LocalName == "ContentProtection"))
            {
                res.Unsupported = "This stream is DRM protected and cannot be downloaded.";
                return res;
            }

            double total = ParseDur(At(root, "mediaPresentationDuration"));
            var mpdBase = Base(mpdUri, root);
            var period = Els(root, "Period").FirstOrDefault();
            if (period == null) { res.Unsupported = "No period found in the MPD."; return res; }

            double pDur = ParseDur(At(period, "duration"));
            if (pDur <= 0) pDur = total;
            var pBase = Base(mpdBase, period);

            foreach (var asEl in Els(period, "AdaptationSet"))
            {
                var asBase = Base(pBase, asEl);
                string? asMime = At(asEl, "mimeType"), asCodecs = At(asEl, "codecs"), asLang = At(asEl, "lang");
                var asTpl = El(asEl, "SegmentTemplate");

                foreach (var rEl in Els(asEl, "Representation"))
                {
                    string mime = At(rEl, "mimeType") ?? asMime ?? "";
                    string? kind = At(asEl, "contentType");
                    if (string.IsNullOrEmpty(kind))
                        kind = mime.StartsWith("video") ? "video" : mime.StartsWith("audio") ? "audio" : "";
                    if (kind != "video" && kind != "audio") continue;

                    var rep = new Rep
                    {
                        Id = At(rEl, "id") ?? "",
                        Kind = kind,
                        Mime = mime,
                        Codecs = At(rEl, "codecs") ?? asCodecs,
                        Lang = asLang
                    };
                    long.TryParse(At(rEl, "bandwidth"), out rep.Bandwidth);
                    int.TryParse(At(rEl, "width") ?? At(asEl, "width"), out rep.W);
                    int.TryParse(At(rEl, "height") ?? At(asEl, "height"), out rep.H);

                    var rBase = Base(asBase, rEl);
                    var rTpl = El(rEl, "SegmentTemplate");
                    var list = El(rEl, "SegmentList") ?? El(asEl, "SegmentList");

                    if (rTpl != null || asTpl != null) BuildFromTemplate(rep, rTpl, asTpl, rBase, pDur);
                    else if (list != null) BuildFromList(rep, list, rBase);
                    else if (rBase != mpdUri) rep.SegUrls.Add(rBase.AbsoluteUri);      // একটাই ফাইল (BaseURL)

                    if (rep.SegUrls.Count > 0) res.Reps.Add(rep);
                }
            }

            if (res.Reps.Count == 0) res.Unsupported = "No downloadable video or audio found in the MPD.";
            return res;
        }

        static void BuildFromTemplate(Rep rep, XElement? rT, XElement? aT, Uri baseUri, double periodSec)
        {
            string? TA(string a) => At(rT, a) ?? At(aT, a);

            string? init = TA("initialization");
            string? media = TA("media");
            double timescale = double.TryParse(TA("timescale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ts) && ts > 0 ? ts : 1;
            long startNumber = long.TryParse(TA("startNumber"), out var sn) ? sn : 1;

            XElement? tl = rT != null ? El(rT, "SegmentTimeline") : null;
            if (tl == null && aT != null) tl = El(aT, "SegmentTimeline");

            if (!string.IsNullOrEmpty(init))
                rep.InitUrl = new Uri(baseUri, Subst(init, rep.Id, rep.Bandwidth, 0, 0)).AbsoluteUri;
            if (string.IsNullOrEmpty(media)) return;

            if (tl != null)
            {
                long time = 0, number = startNumber;
                foreach (var s in Els(tl, "S"))
                {
                    if (long.TryParse(At(s, "t"), out var t)) time = t;
                    if (!long.TryParse(At(s, "d"), out var d) || d <= 0) continue;
                    long r = long.TryParse(At(s, "r"), out var rr) ? rr : 0;
                    long count = r >= 0 ? r + 1 : (long)Math.Ceiling((periodSec * timescale - time) / d);
                    for (long i = 0; i < count; i++)
                    {
                        rep.SegUrls.Add(new Uri(baseUri, Subst(media, rep.Id, rep.Bandwidth, number, time)).AbsoluteUri);
                        time += d;
                        number++;
                    }
                }
            }
            else
            {
                double dur = double.TryParse(TA("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var du) ? du : 0;
                if (dur <= 0 || periodSec <= 0) return;
                long count = (long)Math.Ceiling(periodSec * timescale / dur);
                for (long i = 0; i < count; i++)
                    rep.SegUrls.Add(new Uri(baseUri, Subst(media, rep.Id, rep.Bandwidth, startNumber + i, 0)).AbsoluteUri);
            }
        }

        static void BuildFromList(Rep rep, XElement list, Uri baseUri)
        {
            string? initSrc = At(El(list, "Initialization"), "sourceURL");
            if (!string.IsNullOrEmpty(initSrc)) rep.InitUrl = new Uri(baseUri, initSrc).AbsoluteUri;
            foreach (var su in Els(list, "SegmentURL"))
            {
                string? m = At(su, "media");
                if (!string.IsNullOrEmpty(m)) rep.SegUrls.Add(new Uri(baseUri, m).AbsoluteUri);
            }
        }

        static string Subst(string tpl, string id, long bw, long number, long time)
        {
            return Regex.Replace(tpl, @"\$(RepresentationID|Number|Bandwidth|Time)(?:%0(\d+)d)?\$|\$\$", m =>
            {
                if (m.Value == "$$") return "$";
                string key = m.Groups[1].Value;
                string val = key == "RepresentationID" ? id
                           : key == "Number" ? number.ToString()
                           : key == "Bandwidth" ? bw.ToString()
                           : time.ToString();
                if (m.Groups[2].Success && key != "RepresentationID")
                    val = val.PadLeft(int.Parse(m.Groups[2].Value), '0');
                return val;
            });
        }
    }

    // ====================== ম্যানিফেস্ট পড়ে অপশন বানানো ======================
    public static class MediaParser
    {
        static string Label(int h, int w, long bw, string? codecs)
        {
            var parts = new List<string>();
            parts.Add(h > 0 ? h + "p" : "Auto");
            if (bw > 0) parts.Add((bw / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " Mbps");
            if (!string.IsNullOrEmpty(codecs)) parts.Add(codecs.Split(',')[0].Trim());
            return string.Join("  •  ", parts);
        }

        static string AudioLabel(string? name, string? lang, long bw, string? codecs)
        {
            var parts = new List<string>();
            string n = !string.IsNullOrEmpty(name) ? name : (!string.IsNullOrEmpty(lang) ? lang : "Audio");
            parts.Add(n);
            if (bw > 0) parts.Add((bw / 1000) + " kbps");
            if (!string.IsNullOrEmpty(codecs)) parts.Add(codecs);
            return string.Join("  •  ", parts);
        }

        public static async Task<MediaOptions> LoadAsync(string url, MediaKind kind, string? referer, string? title, CancellationToken ct)
        {
            var uri = new Uri(url);
            string text = await MediaNet.GetTextAsync(url, referer, ct);
            var opts = new MediaOptions { ManifestUrl = url, Referer = referer, Title = title };

            if (kind == MediaKind.Hls)
            {
                opts.Kind = "hls";
                if (Hls.IsMaster(text))
                {
                    var m = Hls.ParseMaster(text, uri);
                    foreach (var v in m.Variants.OrderByDescending(x => x.H).ThenByDescending(x => x.Bandwidth))
                        opts.Videos.Add(new VideoOpt
                        {
                            Url = v.Url,
                            AudioGroup = v.AudioGroup,
                            Height = v.H,
                            Bandwidth = v.Bandwidth,
                            Label = Label(v.H, v.W, v.Bandwidth, v.Codecs)
                        });
                    foreach (var a in m.Audios)
                        opts.Audios.Add(new AudioOpt { Url = a.Url, Group = a.Group, Label = AudioLabel(a.Name, a.Lang, 0, null) });
                }
                else
                {
                    var pl = Hls.ParseMedia(text, uri);
                    if (pl.Unsupported != null) throw new NotSupportedException(pl.Unsupported);
                    opts.Videos.Add(new VideoOpt { Url = url, Label = "Original quality" });
                }
            }
            else
            {
                opts.Kind = "dash";
                var mpd = Dash.Parse(text, uri);
                if (mpd.Unsupported != null) throw new NotSupportedException(mpd.Unsupported);

                foreach (var r in mpd.Reps.Where(q => q.Kind == "video").OrderByDescending(q => q.H).ThenByDescending(q => q.Bandwidth))
                    opts.Videos.Add(new VideoOpt { RepId = r.Id, Height = r.H, Bandwidth = r.Bandwidth, Label = Label(r.H, r.W, r.Bandwidth, r.Codecs) });
                foreach (var r in mpd.Reps.Where(q => q.Kind == "audio").OrderByDescending(q => q.Bandwidth))
                    opts.Audios.Add(new AudioOpt { RepId = r.Id, Bandwidth = r.Bandwidth, Label = AudioLabel(null, r.Lang, r.Bandwidth, r.Codecs) });
            }

            if (opts.Videos.Count == 0 && opts.Audios.Count == 0)
                throw new NotSupportedException("No downloadable streams found.");
            return opts;
        }
    }
}
