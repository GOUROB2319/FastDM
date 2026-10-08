using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FastDM
{
    // ডাউনলোড আইটেমে সেভ হয় (state.json-এ)
    public class YtDlpSpec
    {
        public string? Url { get; set; }
        public string? Format { get; set; }                 // yt-dlp -f সিলেক্টর
        public bool AudioOnly { get; set; }
        public string AudioFormat { get; set; } = "m4a";   // m4a | mp3
        public string? BaseName { get; set; }               // এক্সটেনশন ছাড়া ফাইলের নাম
        public long EstimatedBytes { get; set; }
    }

    // ====================== টুল খুঁজে বের করা ======================
    public static class YtDlpLocator
    {
        public static string? Find() => FindExe("yt-dlp.exe");
        public static string? FindDeno() => FindExe("deno.exe");
        public static string? FindQuickJs() => FindExe("qjs.exe");      // QuickJS / QuickJS-ng (খুব ছোট, Deno-র বিকল্প)
        public static bool HasJsRuntime() => FindQuickJs() != null || FindDeno() != null;

        static string? FindExe(string file)
        {
            string[] local =
            {
                Path.Combine(AppContext.BaseDirectory, "Tools", file),
                Path.Combine(AppContext.BaseDirectory, file)
            };
            foreach (var c in local) if (File.Exists(c)) return c;

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    string p = Path.Combine(dir.Trim().Trim('"'), file);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return null;
        }
    }

    // ====================== yt-dlp কোন কোন সাইটের জন্য ব্যবহার হবে ======================
    public static class YtDlpSites
    {
        static readonly string[] Hosts =
        {
            "youtube.com", "youtu.be", "youtube-nocookie.com", "tiktok.com", "facebook.com", "fb.watch",
            "instagram.com", "twitter.com", "x.com", "vimeo.com", "dailymotion.com", "dai.ly", "reddit.com",
            "twitch.tv", "soundcloud.com", "bilibili.com", "b23.tv", "streamable.com", "rumble.com",
            "odysee.com", "ok.ru", "vk.com", "mixcloud.com", "bandcamp.com", "pinterest.com", "tumblr.com"
        };

        public static bool IsSupported(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
            string host = u.Host.ToLowerInvariant();
            foreach (var h in Hosts)
                if (host == h || host.EndsWith("." + h)) return true;
            return false;
        }
    }

    // ====================== প্রসেস চালানো ======================
    public static class YtDlpRunner
    {
        static ProcessStartInfo Build(string exe, IEnumerable<string> args)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUTF8"] = "1";
            foreach (var a in args) psi.ArgumentList.Add(a);
            return psi;
        }

        // সব কমান্ডে এক: ইউজার কনফিগ উপেক্ষা, JS রানটাইম, ffmpeg, প্রক্সি
        public static List<string> CommonArgs()
        {
            var a = new List<string> { "--ignore-config", "--no-colors" };

            string? qjs = YtDlpLocator.FindQuickJs();
            string? deno = YtDlpLocator.FindDeno();
            if (qjs != null)
            {
                // QuickJS থাকলে সেটাই (ছোট); অন্য রানটাইম বন্ধ রাখি
                a.Add("--no-js-runtimes");
                a.Add("--js-runtimes"); a.Add("quickjs:" + qjs);
            }
            else if (deno != null) { a.Add("--js-runtimes"); a.Add("deno:" + deno); }

            string? ff = FfmpegLocator.Find();
            if (ff != null) { a.Add("--ffmpeg-location"); a.Add(ff); }

            if (ProxyConfig.Mode == ProxyMode.Manual && ProxyConfig.Host.Length > 0)
            {
                string scheme = ProxyConfig.Kind == ProxyKind.Socks5 ? "socks5" : "http";
                string cred = ProxyConfig.User.Length > 0
                    ? Uri.EscapeDataString(ProxyConfig.User) + ":" + Uri.EscapeDataString(ProxyConfig.Pass) + "@"
                    : "";
                a.Add("--proxy"); a.Add(scheme + "://" + cred + ProxyConfig.Host + ":" + ProxyConfig.Port);
            }
            else if (ProxyConfig.Mode == ProxyMode.None)
            {
                a.Add("--proxy"); a.Add("");       // খালি = সরাসরি কানেকশন
            }
            return a;
        }

        public static async Task<int> RunAsync(string exe, IEnumerable<string> args,
                                               Action<string> onOut, Action<string> onErr, CancellationToken ct)
        {
            using var p = new Process { StartInfo = Build(exe, args) };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) onOut?.Invoke(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) onErr?.Invoke(e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
            {
                await p.WaitForExitAsync(ct);
            }
            return p.ExitCode;
        }

        public static string ExtractError(string stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr)) return "yt-dlp failed without an error message.";
            string? last = null;
            foreach (var raw in stderr.Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("ERROR:")) last = l.Substring(6).Trim();
            }
            if (last == null)
                last = stderr.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).LastOrDefault() ?? "yt-dlp failed.";
            return last.Length > 400 ? last.Substring(0, 400) + "…" : last;
        }
    }

    // ====================== ভিডিওর তথ্য (-J) পড়ে কোয়ালিটি তালিকা বানানো ======================
    public class YtVideoOpt
    {
        public string Label = "", Selector = "";
        public long EstBytes;
        public int Height;
    }

    public class YtInfo
    {
        public string Title = "video";
        public string? Extractor, Uploader;
        public double Duration;
        public bool HasAudio;
        public long BestAudioBytes;
        public List<YtVideoOpt> Videos = new List<YtVideoOpt>();
    }

    public static class YtDlpInfo
    {
        class HInfo { public long Bytes; public bool Combined; public int Fps; public string Ext = ""; public double Tbr; }

        public static async Task<YtInfo> LoadAsync(string url, CancellationToken ct)
        {
            string? exe = YtDlpLocator.Find();
            if (exe == null) throw new FileNotFoundException("yt-dlp.exe was not found. Put it in the Tools folder.");

            var args = YtDlpRunner.CommonArgs();
            args.AddRange(new[] { "-J", "--no-playlist", "--no-warnings", "--", url });

            var json = new StringBuilder();
            var err = new StringBuilder();
            int code = await YtDlpRunner.RunAsync(exe, args,
                l => { lock (json) json.AppendLine(l); },
                l => { lock (err) if (err.Length < 8000) err.AppendLine(l); }, ct);

            if (code != 0 || json.Length == 0)
                throw new InvalidOperationException(YtDlpRunner.ExtractError(err.ToString()));

            using var doc = JsonDocument.Parse(json.ToString());
            return Parse(doc.RootElement);
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static double Num(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

        static YtInfo Parse(JsonElement r)
        {
            var info = new YtInfo
            {
                Title = Str(r, "title") ?? "video",
                Extractor = Str(r, "extractor_key") ?? Str(r, "extractor"),
                Uploader = Str(r, "uploader") ?? Str(r, "channel"),
                Duration = Num(r, "duration")
            };

            if (!r.TryGetProperty("formats", out var fmts) || fmts.ValueKind != JsonValueKind.Array)
                return info;

            var byHeight = new Dictionary<int, HInfo>();
            long bestAudio = 0;
            bool hasAudio = false;

            foreach (var f in fmts.EnumerateArray())
            {
                string? v = Str(f, "vcodec"), a = Str(f, "acodec");
                bool hasV = !string.IsNullOrEmpty(v) && v != "none";
                bool hasA = !string.IsNullOrEmpty(a) && a != "none";
                long size = (long)(Num(f, "filesize") > 0 ? Num(f, "filesize") : Num(f, "filesize_approx"));

                if (hasA && !hasV)
                {
                    hasAudio = true;
                    bestAudio = Math.Max(bestAudio, size);
                }
                if (!hasV) continue;

                int h = (int)Num(f, "height");
                if (h <= 0) continue;
                double tbr = Num(f, "tbr");

                if (!byHeight.TryGetValue(h, out var cur) || tbr > cur.Tbr)
                {
                    byHeight[h] = new HInfo
                    {
                        Bytes = size,
                        Combined = hasA,
                        Fps = (int)Num(f, "fps"),
                        Ext = Str(f, "ext") ?? "",
                        Tbr = tbr
                    };
                }
            }

            info.HasAudio = hasAudio;
            info.BestAudioBytes = bestAudio;

            info.Videos.Add(new YtVideoOpt { Label = "Best available", Selector = "bv*+ba/b", Height = 0 });
            foreach (int h in byHeight.Keys.OrderByDescending(x => x))
            {
                var hi = byHeight[h];
                long est = hi.Bytes + (hi.Combined ? 0 : bestAudio);
                string label = h + "p" + (hi.Fps > 30 ? hi.Fps.ToString() : "") +
                               (hi.Ext.Length > 0 ? "  •  " + hi.Ext : "") +
                               (est > 0 ? "  •  ≈ " + Fmt(est) : "");
                info.Videos.Add(new YtVideoOpt
                {
                    Label = label,
                    Height = h,
                    EstBytes = est,
                    Selector = "bv*[height<=" + h + "][ext=mp4]+ba[ext=m4a]/bv*[height<=" + h + "]+ba/b[height<=" + h + "]"
                });
            }

            // কোনো ভিডিও ফরম্যাট না থাকলে (শুধু অডিও সাইট) "Best available" বাদ
            if (byHeight.Count == 0) info.Videos.Clear();
            return info;
        }

        public static string Fmt(double b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (i == 0 ? b.ToString("0") : b.ToString("0.#", CultureInfo.InvariantCulture)) + " " + u[i];
        }
    }

    // ====================== yt-dlp আপডেট (শুধু পোর্টেবল ভার্সনে) ======================
    public static class YtDlpUpdater
    {
        public static async Task<string> UpdateAsync(CancellationToken ct)
        {
            string? exe = YtDlpLocator.Find();
            if (exe == null) return "yt-dlp.exe was not found in the Tools folder.";

            var sb = new StringBuilder();
            await YtDlpRunner.RunAsync(exe, new[] { "--ignore-config", "-U" },
                l => { lock (sb) sb.AppendLine(l); }, l => { lock (sb) sb.AppendLine(l); }, ct);
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "No output from yt-dlp." : s;
        }

        // দিনে একবার চুপচাপ আপডেট চেক (Store ভার্সনে নয়: সেখানে অ্যাপ আপডেটের সাথে আসে)
        public static async Task MaybeAutoUpdateAsync(AppSettings s)
        {
            if (UpdateChecker.IsPackaged) return;
            if ((DateTime.Now - s.LastYtDlpUpdate).TotalHours < 24) return;
            if (YtDlpLocator.Find() == null) return;

            s.LastYtDlpUpdate = DateTime.Now;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try { await UpdateAsync(cts.Token); } catch { }
        }
    }

    // ====================== ডাউনলোড ইঞ্জিন ======================
    public static class YtDlpEngine
    {
        static readonly Regex Intermediate = new Regex(@"\.f\d+(\.|$)|\.part$|\.ytdl$|\.temp$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static long ParseNum(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s == "NA" || s == "None") return 0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)d : 0;
        }

        public static async Task RunAsync(DownloadItem it, int connections, CancellationToken ct)
        {
            var spec = it.YtDlp ?? throw new InvalidOperationException("The yt-dlp specification is missing.");
            string? exe = YtDlpLocator.Find();
            if (exe == null) throw new FileNotFoundException("yt-dlp.exe was not found. Put it in the Tools folder.");
            Directory.CreateDirectory(it.Folder);

            string baseName = !string.IsNullOrEmpty(spec.BaseName) ? spec.BaseName : Path.GetFileNameWithoutExtension(it.FileName);
            string template = Path.Combine(it.Folder, baseName.Replace("%", "%%") + ".%(ext)s");

            var args = YtDlpRunner.CommonArgs();
            args.AddRange(new[]
            {
                "--no-playlist", "--newline", "--progress",
                "--progress-template", "download:FDM|%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s",
                "--retries", "10", "--fragment-retries", "10",
                "-N", Math.Clamp(connections, 1, 8).ToString(),
                "-f", spec.Format ?? "bv*+ba/b",
                "-o", template
            });

            if (spec.AudioOnly)
            {
                args.AddRange(new[] { "-x", "--audio-format", spec.AudioFormat ?? "m4a" });
                if (spec.AudioFormat == "mp3") args.AddRange(new[] { "--audio-quality", "0" });
            }
            else
            {
                args.AddRange(new[] { "--merge-output-format", "mp4" });
            }

            long limit = SpeedLimiter.BytesPerSec;
            if (limit > 0) args.AddRange(new[] { "--limit-rate", Math.Max(1, limit / 1024) + "K" });

            args.Add("--");
            args.Add(spec.Url ?? it.Url);

            var gate = new object();
            long offset = 0, lastDl = 0, lastTotal = 0;
            string? lastError = null;
            var errTail = new StringBuilder();

            void OnLine(string line, bool isErr)
            {
                lock (gate)
                {
                    if (line.StartsWith("FDM|"))
                    {
                        var p = line.Split('|');       // FDM|status|downloaded|total|estimate|speed
                        if (p.Length >= 6)
                        {
                            long dl = ParseNum(p[2]);
                            long tot = ParseNum(p[3]);
                            if (tot <= 0) tot = ParseNum(p[4]);

                            // ভিডিওর পর অডিও শুরু হলে সংখ্যা আবার ছোট হয়: আগেরটা যোগ করে রাখি
                            if (dl < lastDl) offset += lastTotal > 0 ? lastTotal : lastDl;
                            lastDl = dl;
                            lastTotal = tot > 0 ? tot : dl;

                            it.Downloaded = offset + dl;
                            long est = Math.Max(spec.EstimatedBytes, offset + lastTotal);
                            if (est > 0) it.TotalBytes = est;
                            it.StatusNote = "";
                        }
                        return;
                    }

                    if (line.StartsWith("[Merger]") || line.StartsWith("[VideoRemuxer]")) it.StatusNote = "Merging…";
                    else if (line.StartsWith("[ExtractAudio]")) it.StatusNote = "Converting audio…";

                    if (line.StartsWith("ERROR:")) lastError = line.Substring(6).Trim();
                    if (isErr && errTail.Length < 4000) errTail.AppendLine(line);
                }
            }

            it.StatusNote = "Starting…";
            int code = await YtDlpRunner.RunAsync(exe, args, l => OnLine(l, false), l => OnLine(l, true), ct);

            if (code != 0)
                throw new InvalidOperationException(lastError ?? YtDlpRunner.ExtractError(errTail.ToString()));

            string? found = FindOutput(it.Folder, baseName);
            if (found == null) throw new FileNotFoundException("The download finished but the output file was not found.");

            it.FileName = Path.GetFileName(found);
            it.StatusNote = "";
            long size = new FileInfo(found).Length;
            it.TotalBytes = size;
            it.Downloaded = size;
        }

        static string? FindOutput(string folder, string baseName)
        {
            try
            {
                return Directory.GetFiles(folder, baseName + ".*")
                    .Where(f => !Intermediate.IsMatch(Path.GetFileName(f)))
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        // আইটেম মুছলে অসম্পূর্ণ ফাইল (.part, .ytdl, .fNNN) পরিষ্কার
        public static void Cleanup(DownloadItem it)
        {
            if (it.YtDlp == null || string.IsNullOrEmpty(it.Folder)) return;
            try
            {
                string baseName = !string.IsNullOrEmpty(it.YtDlp.BaseName)
                    ? it.YtDlp.BaseName : Path.GetFileNameWithoutExtension(it.FileName);
                foreach (var f in Directory.GetFiles(it.Folder, baseName + ".*"))
                    if (Intermediate.IsMatch(Path.GetFileName(f))) File.Delete(f);
            }
            catch { }
        }
    }
}
