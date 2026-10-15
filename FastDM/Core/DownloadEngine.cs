using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastDM
{
    // ====================== ডাউনলোড ইঞ্জিন ======================
    public static partial class DownloadEngine
    {
        // The shared client never has a cookie jar. External cookies are passed through
        // a client and CookieContainer created for exactly one item.
        static volatile HttpClient Http = CreateClient(null, null);

        // অন্য ফাইল (মিডিয়া/স্ট্রিম) একই HttpClient ব্যবহার করে: প্রক্সি/অথ সব একই থাকে
        public static HttpClient SharedClient => Http;

        // প্রক্সি বদলালে নতুন ক্লায়েন্ট
        public static void ResetClient()
        {
            Http = CreateClient(null, null);
            // Do not dispose the old client here: proxy settings can change during active downloads.
        }

        static HttpClient CreateClient(string? url, IReadOnlyCollection<BridgeCookie>? cookies)
        {
            bool useCookies = cookies != null && cookies.Count > 0;
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                Proxy = DynamicProxy.Instance,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = useCookies,
                CookieContainer = useCookies ? DownloadCookieScope.Create(url ?? string.Empty, cookies) : new CookieContainer()
            };

            var c = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FastDM/1.0");

            return c;
        }

        public static HttpClient CreateCookieClient(string url, IReadOnlyCollection<BridgeCookie>? cookies) => CreateClient(url, cookies);

        // আইটেমের নিজস্ব Referer / User-Agent (থাকলে) রিকোয়েস্টে বসানো
        public static void ApplyItemHeaders(HttpRequestMessage req, DownloadItem it)
        {
            if (!string.IsNullOrEmpty(it.Referer) &&
                Uri.TryCreate(it.Referer, UriKind.Absolute, out var r))
                req.Headers.Referrer = r;

            if (!string.IsNullOrEmpty(it.UserAgent))
                req.Headers.TryAddWithoutValidation("User-Agent", it.UserAgent);
        }

        static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        // Makes a server/user supplied name safe as a Windows file name.
        // May return "" (callers decide the fallback), never "." / ".." or a reserved device name.
        public static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            // Windows drops trailing dots/spaces silently; "." and ".." would point at a folder.
            name = name.Trim().TrimEnd('.', ' ');

            // "CON", "NUL.txt" ... are device names on Windows, even with an extension.
            int dot = name.IndexOf('.');
            string stem = (dot < 0 ? name : name.Substring(0, dot)).TrimEnd();
            if (ReservedNames.Contains(stem))
                name = "_" + name;

            return name;
        }

        public static string NameFromUrl(string url)
        {
            try
            {
                string n = Uri.UnescapeDataString(
                    Path.GetFileName(new Uri(url).AbsolutePath));

                if (!string.IsNullOrWhiteSpace(n))
                {
                    string clean = Sanitize(n);
                    if (clean.Length > 0)
                        return clean;
                }
            }
            catch (Exception exIgnored2) { AppLog.Write("DownloadEngine (ignored): " + exIgnored2.Message); }

            return "download.bin";
        }

        // ফাইলের সাইজ, resume সাপোর্ট আর নাম জানার জন্য
        public static async Task ProbeAsync(DownloadItem it, CancellationToken ct)
        {
            // ftp/sftp হলে আলাদা প্রোব
            if (!IsHttp(it.Url))
            {
                await RemoteTransfer.ProbeAsync(it, ct);
                return;
            }

            using var client = CreateCookieClient(it.Url, it.Cookies);
            using var req = new HttpRequestMessage(HttpMethod.Get, it.Url);
            req.Headers.Range = new RangeHeaderValue(0, 0);

            ApplyAuth(req);
            ApplyItemHeaders(req, it);

            using var resp = await client.SendAsync(
                req,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            CheckAuth(resp);
            resp.EnsureSuccessStatusCode();

            it.ContentType = resp.Content.Headers.ContentType?.MediaType;
            it.ServerTime = resp.Content.Headers.LastModified;

            if (HttpRangeValidation.IsProbeResponse(resp, out long probedTotal))
            {
                it.TotalBytes = probedTotal;
                it.SupportsRange = true;
            }
            else
            {
                it.TotalBytes = resp.Content.Headers.ContentLength ?? 0;
                it.SupportsRange = false;
            }

            if (string.IsNullOrWhiteSpace(it.FileName))
            {
                var cd = resp.Content.Headers.ContentDisposition;
                string? name = cd?.FileNameStar ?? cd?.FileName;

                string clean = string.IsNullOrWhiteSpace(name)
                    ? ""
                    : Sanitize(name.Trim('"'));

                if (clean.Length > 0)
                {
                    it.FileName = clean;
                }
                else
                {
                    var uri = resp.RequestMessage?.RequestUri;
                    it.FileName = NameFromUrl(
                        uri != null ? uri.ToString() : it.Url);
                }
            }
        }

        public static async Task RunAsync(
            DownloadItem it,
            int connections,
            CancellationToken ct)
        {
            Directory.CreateDirectory(it.Folder);
            using var client = CreateCookieClient(it.Url, it.Cookies ?? it.Stream?.Cookies);

            bool fresh =
                it.Segments.Count == 0 ||
                !it.SupportsRange ||
                !File.Exists(it.TempPath) ||
                new FileInfo(it.TempPath).Length != it.TotalBytes;

            if (fresh)
            {
                EnsureFreeSpace(it.Folder, it.TotalBytes);

                it.Segments = new List<Segment>();
                it.Downloaded = 0;

                if (it.SupportsRange && it.TotalBytes > 0)
                {
                    int n = (int)Math.Max(
                        1,
                        Math.Min(
                            connections,
                            it.TotalBytes / (1 << 20)));

                    long size = it.TotalBytes / n;

                    for (int i = 0; i < n; i++)
                    {
                        long start = i * size;
                        long end = (i == n - 1)
                            ? it.TotalBytes - 1
                            : start + size - 1;

                        it.Segments.Add(new Segment
                        {
                            Start = start,
                            End = end
                        });
                    }
                }
                else
                {
                    it.Segments.Add(new Segment
                    {
                        Start = 0,
                        End = it.TotalBytes > 0
                            ? it.TotalBytes - 1
                            : -1
                    });
                }

                using (var fs = new FileStream(
                    it.TempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite))
                {
                    if (it.SupportsRange)
                        fs.SetLength(it.TotalBytes);
                }
            }

            using var linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct);

            var tasks = it.Segments
                .Where(s => !s.Finished)
                .Select(s => Task.Run(async () =>
                {
                    try
                    {
                        // মোট ও প্রতি-সার্ভারের কানেকশন সীমা (Traffic Limits)
                        using (await Traffic.EnterAsync(
                                   HostOf(it.Url),
                                   linked.Token).ConfigureAwait(false))
                        {
                            await SegmentAsync(
                                client,
                                it,
                                s,
                                linked.Token).ConfigureAwait(false);
                        }
                    }
                    catch when (!ct.IsCancellationRequested)
                    {
                        linked.Cancel();
                        throw;
                    }
                }))
                .ToList();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                var faulted = tasks.FirstOrDefault(t => t.IsFaulted);

                throw faulted?.Exception?.GetBaseException()
                    ?? new IOException("Download failed.");
            }

            if (it.SupportsRange &&
                it.Segments.Any(s => !s.Finished))
            {
                throw new IOException("Download incomplete.");
            }

            File.Move(it.TempPath, it.SavePath, overwrite: true);
        }

        // Fails early with a clear message instead of halfway through a big download.
        // The size must fit with 16 MB to spare; network shares and unknown drives are not checked.
        static void EnsureFreeSpace(string folder, long needed)
        {
            if (needed <= 0) return;

            string? root;
            long free;

            try
            {
                root = Path.GetPathRoot(Path.GetFullPath(folder));

                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\"))
                    return;

                free = new DriveInfo(root).AvailableFreeSpace;
            }
            catch
            {
                return;
            }

            if (free < needed + (16L << 20))
                throw new IOException("Not enough free space on " + root + " for this download.");
        }

        static string HostOf(
            string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return "";
            }
        }

        // No byte for this long = dead connection (the HttpClient itself has no timeout).
        static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        // Retry budget: 5 failures in a row; a failure after real progress starts the count again.
        static bool RetryAllowed(ref int attempt, long progressedBytes)
        {
            if (progressedBytes >= (256 << 10))
                attempt = 0;

            return ++attempt < 6;
        }

        // 4xx (404, 403, 410 ...) will not fix itself, so retrying only wastes time.
        static bool IsPermanent(HttpRequestException ex) =>
            ex.StatusCode is { } code &&
            (int)code >= 400 && (int)code < 500 &&
            code != HttpStatusCode.RequestTimeout &&
            code != HttpStatusCode.TooManyRequests;

        static async Task SegmentAsync(
            HttpClient client,
            DownloadItem it,
            Segment seg,
            CancellationToken ct)
        {
            int attempt = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                long before = seg.Downloaded;

                try
                {
                    if (!it.SupportsRange)
                    {
                        it.AddDownloaded(-seg.Downloaded);
                        seg.Downloaded = 0;
                    }

                    long pos = seg.Start + seg.Downloaded;

                    using var req = new HttpRequestMessage(
                        HttpMethod.Get,
                        it.Url);

                    if (it.SupportsRange)
                        req.Headers.Range =
                            new RangeHeaderValue(pos, seg.End);

                    ApplyAuth(req);
                    ApplyItemHeaders(req, it);

                    using var stall =
                        CancellationTokenSource.CreateLinkedTokenSource(ct);

                    stall.CancelAfter(StallTimeout);

                    using var resp = await client.SendAsync(
                        req,
                        HttpCompletionOption.ResponseHeadersRead,
                        stall.Token).ConfigureAwait(false);

                    CheckAuth(resp);
                    resp.EnsureSuccessStatusCode();

                    long expectedRangeBytes = it.SupportsRange
                        ? HttpRangeValidation.ValidateSegmentResponse(resp, pos, seg.End, it.TotalBytes)
                        : -1;

                    using var input =
                        await resp.Content.ReadAsStreamAsync(stall.Token)
                            .ConfigureAwait(false);

                    using var fs = new FileStream(
                        it.TempPath,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        1 << 16,
                        true);

                    fs.Seek(pos, SeekOrigin.Begin);

                    var buf = new byte[1 << 16];
                    int n;

                    while ((n = await input.ReadAsync(
                               buf.AsMemory(),
                               stall.Token).ConfigureAwait(false)) > 0)
                    {
                        // disk write and speed limit may take long on purpose: not a stall
                        stall.CancelAfter(Timeout.InfiniteTimeSpan);

                        if (it.SupportsRange)
                        {
                            long room =
                                seg.End -
                                (seg.Start + seg.Downloaded) +
                                1;

                            if (n > room)
                                throw new InvalidDataException("The server sent more bytes than the requested range.");
                        }

                        await fs.WriteAsync(
                            buf.AsMemory(0, n),
                            ct).ConfigureAwait(false);

                        seg.Downloaded += n;
                        it.AddDownloaded(n);

                        await SpeedLimiter.ThrottleAsync(
                            n,
                            ct).ConfigureAwait(false);

                        if (it.SupportsRange &&
                            seg.Start + seg.Downloaded > seg.End)
                            break;

                        stall.CancelAfter(StallTimeout);
                    }

                    if (!it.SupportsRange)
                        fs.SetLength(fs.Position);

                    if (it.SupportsRange &&
                        seg.Start + seg.Downloaded <= seg.End)
                    {
                        throw new IOException(
                            "Connection closed early.");
                    }

                    if (it.SupportsRange && seg.Downloaded - before != expectedRangeBytes)
                        throw new IOException("Connection returned an incomplete byte range.");

                    seg.Finished = true;
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;      // the user paused/stopped (a stall timeout falls through to retry)
                }
                catch (AuthRequiredException)
                {
                    throw;
                }
                catch (HttpRequestException ex) when (IsPermanent(ex))
                {
                    throw;
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (Exception) when (RetryAllowed(ref attempt, seg.Downloaded - before))
                {
                    await Task.Delay(
                        1500 * attempt,
                        ct).ConfigureAwait(false);
                }
            }
        }
    }
}
