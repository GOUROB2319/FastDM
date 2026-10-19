using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastDM
{
    // HLS / DASH স্ট্রিম ডাউনলোড: সেগমেন্ট আলাদা আলাদা ফাইলে নামিয়ে (resume করা যায়), শেষে ffmpeg দিয়ে জোড়া লাগায়
    public static class StreamEngine
    {
        const int MaxEncryptedSegmentBytes = 16 * 1024 * 1024;

        class PartSeg
        {
            public string Url = "", File = "";
            public string? KeyUrl, IvHex;
            public long Start = -1, Len, Seq;
        }

        class Track
        {
            public string Name = "", Ext = "";
            public bool Fmp4;
            public List<PartSeg> Segs = new List<PartSeg>();
        }

        // ---------- মূল এন্ট্রি ----------
        public static async Task RunAsync(DownloadItem it, int connections, CancellationToken ct)
        {
            var spec = it.Stream ?? throw new InvalidOperationException("The stream specification is missing.");
            using var client = DownloadEngine.CreateCookieClient(it.Url, spec.Cookies ?? it.Cookies);
            Directory.CreateDirectory(it.Folder);
            string partsDir = it.PartsDir;
            Directory.CreateDirectory(partsDir);

            it.StatusNote = "Reading stream…";
            var tracks = await ResolveAsync(spec, ct, client);
            if (tracks.Count == 0) throw new InvalidOperationException("Nothing to download for the selected options.");

            foreach (var tr in tracks)
            {
                string dir = Path.Combine(partsDir, tr.Name);
                Directory.CreateDirectory(dir);
                for (int i = 0; i < tr.Segs.Count; i++)
                    tr.Segs[i].File = Path.Combine(dir, i.ToString("D6") + ".seg");
            }

            var all = tracks.SelectMany(t => t.Segs).ToList();
            it.StreamTotal = all.Count;

            // আগের রান থেকে যেগুলো শেষ হয়ে আছে
            int done = 0;
            long bytes = 0;
            foreach (var s in all)
            {
                if (File.Exists(s.File)) { done++; bytes += new FileInfo(s.File).Length; }
            }
            it.StreamDone = done;
            it.Downloaded = bytes;
            it.LastBytes = bytes;
            UpdateEstimate(it);

            var todo = all.Where(s => !File.Exists(s.File)).ToList();
            it.StatusNote = "";

            var keys = new ConcurrentDictionary<string, byte[]>();
            using var encryptedSlots = new SemaphoreSlim(1, 1);
            int par = Math.Clamp(connections, 1, 16);

            await Parallel.ForEachAsync(todo,
                new ParallelOptions { MaxDegreeOfParallelism = par, CancellationToken = ct },
                async (seg, token) =>
                {
                    await DownloadSegmentAsync(client, it, seg, spec.Referer, keys, encryptedSlots, token);
                    int d = Interlocked.Increment(ref done);
                    it.StreamDone = d;
                    UpdateEstimate(it);
                });

            it.StatusNote = "Merging…";
            string final = await MergeAsync(it, tracks, spec, ct);

            try { Directory.Delete(partsDir, true); } catch (Exception exIgnored1) { AppLog.Write("StreamEngine (ignored): " + exIgnored1.Message); }

            it.StatusNote = "";
            it.StreamDone = it.StreamTotal;
            it.TotalBytes = new FileInfo(final).Length;
            it.Downloaded = it.TotalBytes;
        }

        static void UpdateEstimate(DownloadItem it)
        {
            if (it.StreamTotal > 0 && it.StreamDone > 0)
                it.TotalBytes = (long)(it.Downloaded * (double)it.StreamTotal / it.StreamDone);
        }

        // ---------- ম্যানিফেস্ট থেকে ট্র্যাক বানানো ----------
        static async Task<List<Track>> ResolveAsync(StreamSpec spec, CancellationToken ct, HttpClient client)
        {
            var tracks = new List<Track>();

            if (spec.Kind == "hls")
            {
                bool separateAudio = !string.IsNullOrEmpty(spec.AudioUrl);
                if (!(spec.AudioOnly && separateAudio) && spec.VideoUrl is { Length: > 0 } videoUrl)
                    tracks.Add(await HlsTrackAsync("video", videoUrl, spec.Referer, ct, client));
                if (spec.AudioUrl is { Length: > 0 } audioUrl)
                    tracks.Add(await HlsTrackAsync("audio", audioUrl, spec.Referer, ct, client));
                return tracks;
            }

            // DASH
            string manifestUrl = spec.ManifestUrl ?? throw new InvalidOperationException("The DASH manifest URL is missing.");
            string text = await MediaNet.GetTextAsync(manifestUrl, spec.Referer, ct, client: client);
            var mpd = Dash.Parse(text, new Uri(manifestUrl));
            if (mpd.Unsupported != null) throw new NotSupportedException(mpd.Unsupported);

            if (!spec.AudioOnly && !string.IsNullOrEmpty(spec.VideoRepId))
            {
                var v = mpd.Reps.FirstOrDefault(r => r.Kind == "video" && r.Id == spec.VideoRepId);
                if (v == null) throw new InvalidOperationException("The selected video quality is no longer available.");
                tracks.Add(DashTrack("video", v));
            }
            if (!string.IsNullOrEmpty(spec.AudioRepId))
            {
                var a = mpd.Reps.FirstOrDefault(r => r.Kind == "audio" && r.Id == spec.AudioRepId);
                if (a == null) throw new InvalidOperationException("The selected audio track is no longer available.");
                tracks.Add(DashTrack("audio", a));
            }
            return tracks;
        }

        static async Task<Track> HlsTrackAsync(string name, string url, string? referer, CancellationToken ct, HttpClient client)
        {
            string text = await MediaNet.GetTextAsync(url, referer, ct, client: client);
            var pl = Hls.ParseMedia(text, new Uri(url));
            if (pl.Unsupported != null) throw new NotSupportedException(pl.Unsupported);

            var tr = new Track
            {
                Name = name,
                Fmp4 = pl.Fmp4,
                Ext = pl.Fmp4 ? (name == "audio" ? "m4a" : "mp4") : "ts"
            };
            if (pl.Init != null)
                tr.Segs.Add(new PartSeg { Url = pl.Init.Url, Start = pl.Init.RangeStart, Len = pl.Init.RangeLen, Seq = -1 });
            foreach (var s in pl.Segs)
                tr.Segs.Add(new PartSeg { Url = s.Url, Start = s.RangeStart, Len = s.RangeLen, KeyUrl = s.KeyUrl, IvHex = s.IvHex, Seq = s.Seq });
            return tr;
        }

        static Track DashTrack(string name, Dash.Rep rep)
        {
            bool audio = name == "audio";
            bool webm = rep.Mime != null && rep.Mime.Contains("webm");
            var tr = new Track
            {
                Name = name,
                Fmp4 = true,
                Ext = webm ? (audio ? "weba" : "webm") : (audio ? "m4a" : "mp4")
            };
            long seq = 0;
            if (!string.IsNullOrEmpty(rep.InitUrl)) tr.Segs.Add(new PartSeg { Url = rep.InitUrl, Seq = -1 });
            foreach (var u in rep.SegUrls) tr.Segs.Add(new PartSeg { Url = u, Seq = seq++ });
            return tr;
        }

        // ---------- একটা সেগমেন্ট নামানো (+ দরকার হলে AES-128 ডিক্রিপ্ট) ----------
        static async Task DownloadSegmentAsync(HttpClient client, DownloadItem it, PartSeg s, string? referer,
                                               ConcurrentDictionary<string, byte[]> keys, SemaphoreSlim encryptedSlots,
                                               CancellationToken ct)
        {
            int attempt = 0;
            bool encrypted = !string.IsNullOrEmpty(s.KeyUrl);
            string tmp = s.File + ".tmp";
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                long counted = 0;
                bool slotHeld = false;
                try
                {
                    if (encrypted)
                    {
                        await encryptedSlots.WaitAsync(ct);
                        slotHeld = true;
                    }

                    using var req = new HttpRequestMessage(HttpMethod.Get, s.Url);
                    if (!string.IsNullOrEmpty(referer))
                    {
                        try { req.Headers.Referrer = new Uri(referer); } catch (Exception exIgnored2) { AppLog.Write("StreamEngine (ignored): " + exIgnored2.Message); }
                    }
                    bool ranged = s.Start >= 0 && s.Len > 0;
                    long rangeEnd = 0;
                    if (ranged)
                    {
                        rangeEnd = checked(s.Start + s.Len - 1);
                        req.Headers.Range = new RangeHeaderValue(s.Start, rangeEnd);
                    }
                    DownloadEngine.ApplyAuth(req);

                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    DownloadEngine.CheckAuth(resp);
                    resp.EnsureSuccessStatusCode();
                    if (ranged)
                    {
                        var range = resp.Content.Headers.ContentRange;
                        if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent || range == null ||
                            !string.Equals(range.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
                            range.From != s.Start || range.To != rangeEnd ||
                            (resp.Content.Headers.ContentLength is long rangeLength && rangeLength != s.Len))
                            throw new InvalidDataException("The server returned a mismatched HLS byte range.");
                    }
                    if (encrypted && resp.Content.Headers.ContentLength is long encryptedLength && encryptedLength > MaxEncryptedSegmentBytes)
                        throw new InvalidDataException("The encrypted stream segment exceeds FastDM's 16 MB safety limit.");

                    using var st = await resp.Content.ReadAsStreamAsync(ct);
                    var buf = new byte[1 << 16];
                    if (encrypted)
                    {
                        using var ms = new MemoryStream();
                        int n;
                        while ((n = await st.ReadAsync(buf.AsMemory(), ct)) > 0)
                        {
                            if (ms.Length + n > MaxEncryptedSegmentBytes)
                                throw new InvalidDataException("The encrypted stream segment exceeds FastDM's 16 MB safety limit.");
                            ms.Write(buf, 0, n);
                            counted += n;
                            it.AddDownloaded(n);
                            await SpeedLimiter.ThrottleAsync(n, ct);
                        }
                        if (ranged && counted != s.Len)
                            throw new InvalidDataException("The server returned an incomplete HLS byte range.");

                        byte[] data = await DecryptAsync(client, ms.ToArray(), s, referer, keys, ct);
                        await File.WriteAllBytesAsync(tmp, data, ct);
                    }
                    else
                    {
                        await using var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read,
                            1 << 16, FileOptions.Asynchronous);
                        int n;
                        while ((n = await st.ReadAsync(buf.AsMemory(), ct)) > 0)
                        {
                            if (ranged && counted + n > s.Len)
                                throw new InvalidDataException("The server returned more bytes than the requested HLS range.");
                            await output.WriteAsync(buf.AsMemory(0, n), ct);
                            counted += n;
                            it.AddDownloaded(n);
                            await SpeedLimiter.ThrottleAsync(n, ct);
                        }
                        if (ranged && counted != s.Len)
                            throw new InvalidDataException("The server returned an incomplete HLS byte range.");
                    }

                    File.Move(tmp, s.File, true);
                    return;
                }
                catch (OperationCanceledException)
                {
                    it.AddDownloaded(-counted);
                    TryDeleteTemp(tmp);
                    throw;
                }
                catch (AuthRequiredException)
                {
                    it.AddDownloaded(-counted);
                    TryDeleteTemp(tmp);
                    throw;
                }
                catch (InvalidDataException)
                {
                    it.AddDownloaded(-counted);
                    TryDeleteTemp(tmp);
                    throw;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is { } code &&
                    (int)code >= 400 && (int)code < 500 && code != System.Net.HttpStatusCode.RequestTimeout &&
                    code != System.Net.HttpStatusCode.TooManyRequests)
                {
                    it.AddDownloaded(-counted);
                    TryDeleteTemp(tmp);
                    throw;
                }
                catch when (++attempt < 5)
                {
                    it.AddDownloaded(-counted);            // আবার চেষ্টার আগে গোনা বাইট ফেরত
                    TryDeleteTemp(tmp);
                    await Task.Delay(1000 * attempt, ct);
                }
                catch
                {
                    it.AddDownloaded(-counted);
                    TryDeleteTemp(tmp);
                    throw;
                }
                finally
                {
                    if (slotHeld) encryptedSlots.Release();
                }
            }
        }

        static void TryDeleteTemp(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { AppLog.Write("StreamEngine temp cleanup failed: " + ex.Message); }
        }

        static async Task<byte[]> DecryptAsync(HttpClient client, byte[] data, PartSeg s, string? referer,
                                               ConcurrentDictionary<string, byte[]> keys, CancellationToken ct)
        {
            string keyUrl = s.KeyUrl ?? throw new InvalidOperationException("The encryption key URL is missing.");
            if (!keys.TryGetValue(keyUrl, out var key))
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, keyUrl);
                if (!string.IsNullOrEmpty(referer))
                {
                    try { req.Headers.Referrer = new Uri(referer); } catch (Exception exIgnored3) { AppLog.Write("StreamEngine (ignored): " + exIgnored3.Message); }
                }
                DownloadEngine.ApplyAuth(req);
                using var resp = await client.SendAsync(req, ct);
                DownloadEngine.CheckAuth(resp);
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentLength is long keyLength && keyLength != 16)
                    throw new InvalidDataException("Unsupported encryption key.");
                using var keyStream = await resp.Content.ReadAsStreamAsync(ct);
                var keyBuffer = new byte[17];
                int keyBytes = 0;
                while (keyBytes < keyBuffer.Length)
                {
                    int n = await keyStream.ReadAsync(keyBuffer.AsMemory(keyBytes), ct);
                    if (n == 0) break;
                    keyBytes += n;
                }
                if (keyBytes != 16) throw new InvalidDataException("Unsupported encryption key.");
                key = keyBuffer.AsSpan(0, 16).ToArray();
                keys[keyUrl] = key;
            }

            byte[] iv = new byte[16];
            if (!string.IsNullOrEmpty(s.IvHex))
            {
                string hex = s.IvHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.IvHex.Substring(2) : s.IvHex;
                hex = hex.PadLeft(32, '0');
                iv = Convert.FromHexString(hex);
            }
            else
            {
                long seq = s.Seq;                       // IV না থাকলে সেগমেন্ট সিকোয়েন্স নম্বর (big-endian)
                for (int i = 15; i >= 8; i--) { iv[i] = (byte)(seq & 0xFF); seq >>= 8; }
            }

            using var aes = Aes.Create();
            aes.Key = key;
            return aes.DecryptCbc(data, iv, PaddingMode.PKCS7);
        }

        // ---------- জোড়া লাগানো ----------
        static async Task<string> MergeAsync(DownloadItem it, List<Track> tracks, StreamSpec spec, CancellationToken ct)
        {
            string partsDir = it.PartsDir;

            // ১) প্রতি ট্র্যাকের সেগমেন্টগুলো ক্রমে জুড়ে একটা ফাইল
            var files = new List<KeyValuePair<Track, string>>();
            foreach (var tr in tracks)
            {
                string path = Path.Combine(partsDir, tr.Name + "." + tr.Ext);
                using (var outFs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                {
                    foreach (var s in tr.Segs)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var inFs = new FileStream(s.File, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
                        await inFs.CopyToAsync(outFs, 1 << 16, ct);
                    }
                }
                files.Add(new KeyValuePair<Track, string>(tr, path));
            }

            string baseName = Path.Combine(it.Folder, Path.GetFileNameWithoutExtension(it.FileName));
            string finalExt = spec.AudioOnly ? "m4a" : "mp4";
            string? ff = FfmpegLocator.Find();
            string? note = null;

            // ২) ffmpeg আছে: স্ট্রিম কপি করে একটাই MP4/M4A
            if (ff != null)
            {
                string outPath = baseName + "." + finalExt;
                var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
                foreach (var f in files) { args.Add("-i"); args.Add(f.Value); }

                if (spec.AudioOnly) args.AddRange(new[] { "-vn", "-c:a", "copy" });
                else if (files.Count == 2) args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0", "-c", "copy" });
                else args.AddRange(new[] { "-c", "copy" });

                if (!spec.AudioOnly) { args.Add("-movflags"); args.Add("+faststart"); }
                args.Add(outPath);

                var (code, err) = await RunFfmpegAsync(ff, args, ct);
                if (code == 0 && File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                {
                    it.FileName = Path.GetFileName(outPath);
                    return outPath;
                }
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch (Exception exIgnored4) { AppLog.Write("StreamEngine (ignored): " + exIgnored4.Message); }
                note = "Saved without merging (ffmpeg: " + Shorten(err) + ")";
            }
            else note = "Saved without merging (ffmpeg not found).";

            // ৩) ffmpeg নেই/ব্যর্থ: কাঁচা ফাইল হিসেবে রাখা (একটা ট্র্যাক হলে সেটাই, দুটো হলে আলাদা)
            string? primary = null;
            foreach (var f in files)
            {
                string suffix = files.Count == 2 ? "." + f.Key.Name : "";
                string dest = Unique(baseName + suffix + "." + f.Key.Ext);
                File.Move(f.Value, dest);
                if (primary == null) primary = dest;
            }
            if (primary == null) throw new InvalidOperationException("No stream files were produced.");
            it.FileName = Path.GetFileName(primary);
            it.Error = note;
            return primary;
        }

        static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string? dir = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                string cand = Path.Combine(dir ?? throw new ArgumentException("The path must include a directory.", nameof(path)), stem + " (" + i + ")" + ext);
                if (!File.Exists(cand)) return cand;
            }
        }

        static string Shorten(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unknown error";
            s = s.Trim().Replace("\r", " ").Replace("\n", " ");
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        static async Task<KeyValuePair<int, string>> RunFfmpegAsync(string exe, IEnumerable<string> args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            p.Start();
            Task output = ProcessOutput.DrainLinesAsync(p.StandardOutput, _ => { });
            Task error = ProcessOutput.DrainLinesAsync(p.StandardError, line => ProcessOutput.AppendBoundedTail(sb, line, 4000));

            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch (Exception exIgnored5) { AppLog.Write("StreamEngine (ignored): " + exIgnored5.Message); } }))
            {
                try { await p.WaitForExitAsync(ct); }
                catch
                {
                    try { if (!p.HasExited) p.Kill(true); } catch (Exception exIgnored6) { AppLog.Write("StreamEngine cleanup failed: " + exIgnored6.Message); }
                    await p.WaitForExitAsync(CancellationToken.None);
                    await Task.WhenAll(output, error);
                    throw;
                }
            }
            await Task.WhenAll(output, error);
            return new KeyValuePair<int, string>(p.ExitCode, sb.ToString());
        }
    }
}
