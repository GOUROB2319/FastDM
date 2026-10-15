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
        string? url;
        public string? Url
        {
            get => url;
            set
            {
                UrlHadCredentials = Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo);
                url = UrlCredential.StripAndRemember(value);
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool UrlHadCredentials { get; private set; }
        public string? Format { get; set; }                 // yt-dlp -f সিলেক্টর
        public bool AudioOnly { get; set; }
        public bool IgnoreCookies { get; set; }
        public string AudioFormat { get; set; } = "m4a";   // m4a | mp3
        public string? BaseName { get; set; }               // এক্সটেনশন ছাড়া ফাইলের নাম
        public long EstimatedBytes { get; set; }
        public bool NameFromTitle { get; set; }             // true: the file is named after the video title (used by batch imports); BaseName is not needed

        [System.Text.Json.Serialization.JsonIgnore]
        public string? InfoJsonPath { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset InfoJsonExpiresAt { get; set; }
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
            // Do not execute an arbitrary yt-dlp/ffmpeg found through PATH. Tool discovery is
            // app-local so packaged builds and clean clones have predictable provenance.
            return null;
        }
    }

    // Which browser yt-dlp reads login cookies from (Preferences -> Advanced -> Video sites).
    // Only values from the fixed list below are ever passed to yt-dlp, so a hand-edited
    // state.json cannot inject extra command-line options.
    public static class YtDlpCookies
    {
        public static readonly string[] Browsers = { "", "edge", "chrome", "firefox", "brave" };
        public static readonly string[] Labels = { "None", "Microsoft Edge", "Google Chrome", "Mozilla Firefox", "Brave" };

        static volatile string browser = "";
        static volatile string cookieFile = "";

        public static string Browser => browser;

        // Path of an exported cookies.txt (Netscape format). Takes priority over the browser choice,
        // because newer Chrome/Edge/Brave on Windows block yt-dlp from reading their cookie database.
        public static string CookieFile => cookieFile;

        public static string Normalize(string? value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            return Array.IndexOf(Browsers, v) >= 0 ? v : "";
        }

        public static void Apply(AppSettings s)
        {
            browser = Normalize(s.YtCookieBrowser);
            cookieFile = (s.YtCookieFile ?? "").Trim().Trim('"');
            YtDlpPreviewCache.Shared.Clear();
        }

        public static YtDlpCookieSource SelectSource() =>
            YtDlpDiagnostics.SelectCookieSource(cookieFile, browser);

        // Returns a short "what to do next" text when the error looks like a sign-in/cookie problem, else null.
        public static string? HintFor(string? message)
        {
            string m = (message ?? "").ToLowerInvariant();
            bool signIn = m.Contains("not a bot") || m.Contains("sign in to confirm");
            bool cookie = m.Contains("cookie");
            var source = SelectSource();
            var kind = YtDlpDiagnostics.Classify(message, source);
            if (!signIn && !cookie && kind is not YtDlpFailureKind.MissingCookieFile and not YtDlpFailureKind.InvalidCookieFile)
                return null;
            return "\n\n" + YtDlpDiagnostics.Describe(kind, UpdateChecker.IsPackaged).NextStep;
        }
    }


    // ====================== প্রসেস চালানো ======================
    public static class YtDlpRunner
    {
        const int ProcessCapacity = 4;
        static readonly SemaphoreSlim ProcessLimit = new SemaphoreSlim(ProcessCapacity, ProcessCapacity);
        static readonly object ProcessStateGate = new object();
        static bool updatePending;

        static ProcessStartInfo Build(string exe, IEnumerable<string> args, bool redirectInput)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (redirectInput) psi.StandardInputEncoding = Encoding.UTF8;
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUTF8"] = "1";
            psi.Environment["PATH"] = YtDlpProcessEnvironment.BuildRestrictedPath(new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Tools"),
                AppContext.BaseDirectory,
                Environment.SystemDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            });
            YtDlpProxy.ApplyEnvironment(psi, ProxyConfig.Mode, ProxyConfig.Kind,
                ProxyConfig.Host, ProxyConfig.Port, ProxyConfig.User, ProxyConfig.Pass);
            foreach (var a in args) psi.ArgumentList.Add(a);
            return psi;
        }

        // সব কমান্ডে এক: ইউজার কনফিগ উপেক্ষা, JS রানটাইম, ffmpeg, প্রক্সি
        public static List<string> CommonArgs(bool omitCookies = false)
        {
            var a = new List<string> { "--ignore-config", "--no-colors" };
            a.AddRange(YtDlpRetryPolicy.CopyArguments());

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

            a.AddRange(YtDlpProxy.BuildArguments(ProxyConfig.Mode, ProxyConfig.Kind,
                ProxyConfig.Host, ProxyConfig.Port, ProxyConfig.User));

            // Login cookies for sites that ask "Sign in to confirm you're not a bot".
            // yt-dlp reads them itself, on this PC only; FastDM never stores them. A cookies.txt file wins over a browser.
            var cookieSource = omitCookies
                ? new YtDlpCookieSource { Kind = YtDlpCookieSourceKind.None }
                : YtDlpCookies.SelectSource();
            a.AddRange(YtDlpDiagnostics.CookieArguments(cookieSource, omitCookies));
            return a;
        }

        public static async Task<int> RunAsync(string exe, IEnumerable<string> args,
                                               Action<string> onOut, Action<string> onErr, CancellationToken ct,
                                               string? standardInput = null)
        {
            await WaitForProcessSlotAsync(ct);
            try
            {
                return await RunProcessAsync(exe, args, onOut, onErr, ct, standardInput);
            }
            finally { ProcessLimit.Release(); }
        }

        public static async Task<int> RunExclusiveUpdateAsync(string exe, IEnumerable<string> args,
                                                               Action<string> onOut, Action<string> onErr, CancellationToken ct)
        {
            lock (ProcessStateGate)
            {
                if (updatePending) throw new InvalidOperationException("A yt-dlp update is already in progress.");
                updatePending = true;
            }

            int acquiredSlots = 0;
            try
            {
                for (; acquiredSlots < ProcessCapacity; acquiredSlots++)
                    await ProcessLimit.WaitAsync(ct);
                return await RunProcessAsync(exe, args, onOut, onErr, ct, null);
            }
            finally
            {
                while (acquiredSlots-- > 0) ProcessLimit.Release();
                lock (ProcessStateGate) updatePending = false;
            }
        }

        static async Task WaitForProcessSlotAsync(CancellationToken ct)
        {
            while (true)
            {
                await ProcessLimit.WaitAsync(ct);
                lock (ProcessStateGate)
                {
                    if (!updatePending) return;
                }
                ProcessLimit.Release();
                await Task.Delay(100, ct);
            }
        }

        static async Task<int> RunProcessAsync(string exe, IEnumerable<string> args,
                                                Action<string> onOut, Action<string> onErr, CancellationToken ct,
                                                string? standardInput)
        {
            using var p = new Process { StartInfo = Build(exe, args, standardInput != null) };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) onOut?.Invoke(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) onErr?.Invoke(e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch (Exception exIgnored2) { AppLog.Write("YtDlp (ignored): " + exIgnored2.Message); } }))
            {
                try
                {
                    if (standardInput != null)
                    {
                        await p.StandardInput.WriteAsync(standardInput.AsMemory(), ct);
                        await p.StandardInput.FlushAsync(ct);
                        p.StandardInput.Close();
                    }
                    await p.WaitForExitAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    try { if (!p.HasExited) p.Kill(true); } catch (Exception exIgnored3) { AppLog.Write("YtDlp (ignored): " + exIgnored3.Message); }
                    await p.WaitForExitAsync(CancellationToken.None);
                    p.WaitForExit();
                    throw;
                }
                catch
                {
                    try { if (!p.HasExited) p.Kill(true); } catch (Exception exIgnored4) { AppLog.Write("YtDlp cleanup failed: " + exIgnored4.Message); }
                    await p.WaitForExitAsync(CancellationToken.None);
                    throw;
                }
            }
            p.WaitForExit(); // drain async stdout/stderr callbacks before returning
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
            last = AppLog.Redact(last);
            return last.Length > 400 ? last.Substring(0, 400) + "…" : last;
        }
    }

    // ====================== ভিডিও/প্লেলিস্টের তথ্য (-J) ======================
    public static class YtDlpInfo
    {
        const int MaxMetadataBytes = 16 * 1024 * 1024;

        public static async Task<YtInfo> LoadAsync(string url, CancellationToken ct, IProgress<string>? progress = null, bool omitCookies = false)
        {
            var args = YtDlpRunner.CommonArgs(omitCookies);
            args.AddRange(new[] { "-J", "--no-playlist" });
            var urlInput = YtDlpUrlInput.Create(url);
            args.AddRange(urlInput.Arguments);
            var (json, diagnostic) = await LoadJsonAsync("video", args, urlInput.StandardInput, ct, progress);
            var info = YtDlpMetadataParser.ParseVideo(json);
            info.RawInfoJson = json;
            var warningKind = YtDlpDiagnostics.Classify(diagnostic);
            if (info.Videos.Count == 0 && !info.HasAudio)
            {
                if (warningKind != YtDlpFailureKind.Generic && warningKind != YtDlpFailureKind.FormatUnavailable)
                    throw new YtDlpProcessException(0, diagnostic);
                throw new YtDlpProcessException(0, "ERROR: requested format is not available; yt-dlp returned no downloadable formats.");
            }
            return info;
        }

        public static async Task<YtPlaylistInfo> LoadPlaylistAsync(string url, CancellationToken ct, IProgress<string>? progress = null, int maxEntries = 100, bool omitCookies = false)
        {
            var args = YtDlpRunner.CommonArgs(omitCookies);
            maxEntries = Math.Clamp(maxEntries, 1, 1000);
            args.AddRange(new[] { "--yes-playlist", "--flat-playlist", "--playlist-end", maxEntries.ToString(CultureInfo.InvariantCulture), "-J" });
            var urlInput = YtDlpUrlInput.Create(url);
            args.AddRange(urlInput.Arguments);
            var (json, diagnostic) = await LoadJsonAsync("playlist", args, urlInput.StandardInput, ct, progress);
            var info = YtDlpMetadataParser.ParsePlaylist(json);
            info.EntriesLimit = maxEntries;
            if (info.Entries.Count == 0)
            {
                if (diagnostic.Length > 0 && YtDlpDiagnostics.Classify(diagnostic) != YtDlpFailureKind.Generic)
                    throw new YtDlpProcessException(0, diagnostic);
                throw new InvalidOperationException("yt-dlp returned no playlist entries.");
            }
            return info;
        }

        static async Task<(string Json, string Diagnostic)> LoadJsonAsync(string operation, List<string> args, string? standardInput,
                                                                          CancellationToken ct, IProgress<string>? progress)
        {
            string? exe = YtDlpLocator.Find();
            if (exe == null) throw new FileNotFoundException("yt-dlp.exe was not found. Put it in the Tools folder.");

            var json = new StringBuilder();
            var err = new StringBuilder();
            bool tooLarge = false;
            int metadataBytes = 0;
            int newlineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
            var watch = Stopwatch.StartNew();
            int code = await YtDlpRunner.RunAsync(exe, args,
                line =>
                {
                    lock (json)
                    {
                        if (tooLarge) return;
                        if (line.Length > MaxMetadataBytes)
                        {
                            tooLarge = true;
                            return;
                        }
                        int lineBytes = Encoding.UTF8.GetByteCount(line);
                        if (lineBytes <= MaxMetadataBytes - metadataBytes - newlineBytes)
                        {
                            json.AppendLine(line);
                            metadataBytes += lineBytes + newlineBytes;
                        }
                        else tooLarge = true;
                    }
                },
                line =>
                {
                    lock (err) if (err.Length < 32000) err.AppendLine(line);
                    string label = YtDlpDiagnostics.ProgressLabel(line);
                    if (label.Length > 0) progress?.Report(label);
                }, ct, standardInput);
            watch.Stop();

            string runtime = YtDlpLocator.FindQuickJs() != null ? "quickjs" : YtDlpLocator.FindDeno() != null ? "deno" : "none";
            var cookieSource = YtDlpCookies.SelectSource();
            string cookieLabel = cookieSource.Kind switch
            {
                YtDlpCookieSourceKind.CookieFile => "file",
                YtDlpCookieSourceKind.Browser => cookieSource.Browser ?? "browser",
                _ => "off"
            };
            AppLog.Write("yt-dlp " + operation + " metadata: " + watch.Elapsed.TotalSeconds.ToString("0.0") +
                         " s, exit " + code + ", js runtime " + runtime + ", cookies " + cookieLabel);

            if (tooLarge)
                throw new InvalidOperationException("The video or playlist metadata exceeded FastDM's 16 MB safety limit.");
            if (code != 0 || json.Length == 0)
                throw new YtDlpProcessException(code, err.ToString());
            return (json.ToString(), err.ToString());
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
            int code = await YtDlpRunner.RunExclusiveUpdateAsync(exe, new[] { "--ignore-config", "-U" },
                l => { lock (sb) sb.AppendLine(l); }, l => { lock (sb) sb.AppendLine(l); }, ct);
            string s = sb.ToString().Trim();
            if (code != 0)
                throw new InvalidOperationException("yt-dlp update failed (exit code " + code + "): " + YtDlpRunner.ExtractError(s));
            return s.Length == 0 ? "yt-dlp reported success with no output." : AppLog.Redact(s);
        }

        // দিনে একবার চুপচাপ আপডেট চেক (Store ভার্সনে নয়: সেখানে অ্যাপ আপডেটের সাথে আসে)
        public static async Task MaybeAutoUpdateAsync(AppSettings s)
        {
            if (UpdateChecker.IsPackaged) return;
            if ((DateTime.Now - s.LastYtDlpUpdate).TotalHours < 24) return;
            if ((DateTime.Now - s.LastYtDlpAttempt).TotalHours < 1) return;
            if (YtDlpLocator.Find() == null) return;

            s.LastYtDlpAttempt = DateTime.Now;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                await UpdateAsync(cts.Token);
                s.LastYtDlpUpdate = DateTime.Now;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exIgnored3) { AppLog.Write("YtDlp update failed (ignored): " + exIgnored3.Message); }
        }
    }

    // ====================== ডাউনলোড ইঞ্জিন ======================
    public static class YtDlpEngine
    {
        static Regex Intermediate => YtDlpOutput.Intermediate;

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

            bool fromTitle = spec.NameFromTitle;
            string itemDir = Path.Combine(it.Folder, YtDlpOutput.ItemDirName(it.Id));

            string baseName = !string.IsNullOrEmpty(spec.BaseName) ? spec.BaseName : Path.GetFileNameWithoutExtension(it.FileName);
            string template = fromTitle
                ? "%(title).150B.%(ext)s"
                : Path.Combine(it.Folder, baseName.Replace("%", "%%") + ".%(ext)s");

            var args = YtDlpRunner.CommonArgs(spec.IgnoreCookies);
            args.AddRange(new[]
            {
                "--no-playlist", "--newline", "--progress",
                "--progress-template", "download:FDM|%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(info.vcodec)s|%(info.acodec)s",
                "-N", Math.Clamp(connections, 1, 8).ToString(),
                "-f", spec.Format ?? "bv*+ba/b",
                "-o", template
            });

            // Named after the title: yt-dlp works in a private folder for this download (so partial files can
            // resume and never mix with other downloads); FastDM moves the finished file out afterwards.
            if (fromTitle)
            {
                Directory.CreateDirectory(itemDir);
                try { File.SetAttributes(itemDir, File.GetAttributes(itemDir) | FileAttributes.Hidden); } catch (Exception exIgnored4) { AppLog.Write("YtDlp (ignored): " + exIgnored4.Message); }
                args.AddRange(new[] { "-P", itemDir, "--windows-filenames" });
            }

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

            string url = spec.Url ?? it.Url;
            bool canUseInfoJson = !string.IsNullOrWhiteSpace(spec.InfoJsonPath) &&
                spec.InfoJsonExpiresAt > DateTimeOffset.UtcNow && File.Exists(spec.InfoJsonPath);
            string? activeInfoJson = canUseInfoJson ? spec.InfoJsonPath : null;
            var urlInput = YtDlpUrlInput.Create(url);
            var downloadArgs = new List<string>(args);
            if (canUseInfoJson && activeInfoJson != null)
                downloadArgs.AddRange(new[] { "--load-info-json", activeInfoJson });
            else
                downloadArgs.AddRange(urlInput.Arguments);

            string? standardInput = canUseInfoJson ? null : urlInput.StandardInput;
            var gate = new object();
            var errTail = new StringBuilder();
            var progress = new YtDlpProgressParser();
            string? lastError = null;

            void OnLine(string line, bool isErr)
            {
                lock (gate)
                {
                    if (progress.TryParseProgress(line, out var update))
                    {
                        if (progress.Stage is not YtDlpStage.Merging and not YtDlpStage.PostProcessing)
                        {
                            it.ProgressStage = update.Stage;
                            it.StatusNote = YtDlpRetryPolicy.StageLabel(update.Stage);
                            it.Downloaded = update.Downloaded;
                            long estimate = Math.Max(spec.EstimatedBytes, update.Total);
                            if (estimate > 0) it.TotalBytes = estimate;
                            it.Speed = update.Speed;
                        }
                        return;
                    }

                    YtDlpStage observed = progress.ObserveMessage(line);
                    if (observed is YtDlpStage.Merging or YtDlpStage.PostProcessing)
                    {
                        it.ProgressStage = observed;
                        it.StatusNote = YtDlpRetryPolicy.StageLabel(observed) + "…";
                        it.Speed = 0;
                    }
                    if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)) lastError = line.Substring(6).Trim();
                    if (isErr && errTail.Length < 4000) errTail.AppendLine(line);
                }
            }

            it.ProgressStage = YtDlpStage.Starting;
            it.StatusNote = YtDlpRetryPolicy.StageLabel(it.ProgressStage) + "…";
            int code = await YtDlpRunner.RunAsync(exe, downloadArgs, l => OnLine(l, false), l => OnLine(l, true), ct, standardInput);
            string diagnostic = errTail.Length > 0 ? errTail.ToString() : lastError ?? "";

            if (code != 0 && canUseInfoJson &&
                YtDlpRetryPolicy.ShouldRefreshInfoJson(YtDlpDiagnostics.Classify(diagnostic), diagnostic))
            {
                YtDlpPreviewCache.DeleteInfoJson(activeInfoJson);
                YtDlpPreviewCache.Shared.Remove(url, spec.IgnoreCookies);
                spec.InfoJsonPath = null;
                errTail.Clear();
                lastError = null;
                progress = new YtDlpProgressParser();
                it.ProgressStage = YtDlpStage.Retrying;
                it.StatusNote = "Refreshing video details…";
                downloadArgs = new List<string>(args);
                downloadArgs.AddRange(urlInput.Arguments);
                code = await YtDlpRunner.RunAsync(exe, downloadArgs, l => OnLine(l, false), l => OnLine(l, true), ct, urlInput.StandardInput);
                diagnostic = errTail.Length > 0 ? errTail.ToString() : lastError ?? "";
            }

            if (code != 0)
            {
                YtDlpPreviewCache.Shared.Remove(url, spec.IgnoreCookies);
                throw new YtDlpProcessException(code, diagnostic);
            }
            string? found;
            if (fromTitle)
            {
                string? made = YtDlpOutput.FindFinal(itemDir);
                found = made == null ? null : YtDlpOutput.MoveToUnique(made, it.Folder);

                if (found != null)
                {
                    try { Directory.Delete(itemDir, true); } catch (Exception exIgnored5) { AppLog.Write("YtDlp (ignored): " + exIgnored5.Message); }
                }
            }
            else
            {
                found = FindOutput(it.Folder, baseName);
            }

            it.ProgressStage = YtDlpStage.VerifyingOutput;
            it.StatusNote = YtDlpRetryPolicy.StageLabel(it.ProgressStage) + "…";
            if (found == null)
            {
                YtDlpPreviewCache.Shared.Remove(url, spec.IgnoreCookies);
                throw new FileNotFoundException("The download finished but the output file was not found.");
            }

            var outputInfo = new FileInfo(found);
            if (!outputInfo.Exists || outputInfo.Length <= 0)
            {
                try { if (outputInfo.Exists) outputInfo.Delete(); }
                catch (Exception exIgnored7) { AppLog.Write("YtDlp could not remove empty output: " + exIgnored7.Message); }
                YtDlpPreviewCache.Shared.Remove(url, spec.IgnoreCookies);
                throw new InvalidDataException("yt-dlp created an empty output file; the download is not complete.");
            }

            it.FileName = Path.GetFileName(found);
            it.StatusNote = "";
            it.ProgressStage = YtDlpStage.None;
            long size = outputInfo.Length;
            it.TotalBytes = size;
            it.Downloaded = size;
            YtDlpPreviewCache.Shared.Remove(url, spec.IgnoreCookies);
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
                string privateDir = Path.Combine(it.Folder, YtDlpOutput.ItemDirName(it.Id));
                if (Directory.Exists(privateDir)) Directory.Delete(privateDir, true);
            }
            catch (Exception exIgnored6) { AppLog.Write("YtDlp (ignored): " + exIgnored6.Message); }

            try
            {
                string baseName = !string.IsNullOrEmpty(it.YtDlp.BaseName)
                    ? it.YtDlp.BaseName : Path.GetFileNameWithoutExtension(it.FileName);
                foreach (var f in Directory.GetFiles(it.Folder, baseName + ".*"))
                    if (Intermediate.IsMatch(Path.GetFileName(f))) File.Delete(f);
            }
            catch (Exception exIgnored7) { AppLog.Write("YtDlp (ignored): " + exIgnored7.Message); }
        }
    }
}
