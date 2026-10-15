using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using FastDM;

if (args.Length == 1 && args[0] == "--emit-unicode")
{
    Console.OutputEncoding = new UTF8Encoding(false);
    Console.WriteLine("ASCII you’re not a bot — বাংলা ভিডিও");
    return 0;
}
if (args.Length == 1 && args[0] == "--sleep")
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}
if (args.Length == 1 && args[0] == "--sleep-short")
{
    await Task.Delay(TimeSpan.FromMilliseconds(700));
    return 0;
}
if (args.Length == 3 && args[0] == "--wait-for-release")
{
    string barrier = args[1];
    File.WriteAllText(Path.Combine(barrier, args[2]), "started");
    while (!File.Exists(Path.Combine(barrier, "release"))) await Task.Delay(10);
    return 0;
}

var failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
    if (!ok) failures++;
}

string root = Path.Combine(Path.GetTempPath(), "fastdm-engine-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string processHost = Environment.ProcessPath ?? throw new InvalidOperationException("The integration executable path is unavailable.");
    var unicodeOutput = new StringBuilder();
    var unicodeError = new StringBuilder();
    int unicodeExit = await YtDlpRunner.RunAsync(processHost,
        new[] { "--emit-unicode" },
        line => { lock (unicodeOutput) unicodeOutput.AppendLine(line); }, line => { lock (unicodeError) unicodeError.AppendLine(line); }, CancellationToken.None);
    Check("yt-dlp process runner decodes ASCII, Unicode punctuation and Bangla output (exit " + unicodeExit + "; " + unicodeOutput.ToString().Trim() + "; " + unicodeError.ToString().Trim() + ")",
        unicodeExit == 0 && unicodeOutput.ToString().Contains("ASCII you’re not a bot — বাংলা ভিডিও"));

    bool processCancelled = false;
    using (var processCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
    {
        try
        {
            await YtDlpRunner.RunAsync(processHost,
                new[] { "--sleep" },
                _ => { }, _ => { }, processCancellation.Token);
        }
        catch (OperationCanceledException) { processCancelled = true; }
    }
    Check("yt-dlp runner terminates a cancelled child process", processCancelled);

    var activeChild = YtDlpRunner.RunAsync(processHost, new[] { "--sleep-short" }, _ => { }, _ => { }, CancellationToken.None);
    await Task.Delay(80);
    var exclusiveOutput = new StringBuilder();
    var exclusiveUpdate = YtDlpRunner.RunExclusiveUpdateAsync(processHost, new[] { "--emit-unicode" },
        line => { lock (exclusiveOutput) exclusiveOutput.AppendLine(line); }, _ => { }, CancellationToken.None);
    await Task.Delay(80);
    bool updateWaited = !exclusiveUpdate.IsCompleted;
    var queuedOutput = new StringBuilder();
    var queuedChild = YtDlpRunner.RunAsync(processHost, new[] { "--emit-unicode" },
        line => { lock (queuedOutput) queuedOutput.AppendLine(line); }, _ => { }, CancellationToken.None);
    int[] childResults = await Task.WhenAll(activeChild, exclusiveUpdate, queuedChild);
    Check("exclusive yt-dlp update waits for active children and serializes new processes",
        updateWaited && childResults.All(x => x == 0) && exclusiveOutput.Length > 0 && queuedOutput.Length > 0);

    byte[] payload = Enumerable.Range(0, 1_300_000).Select(i => (byte)(i % 251)).ToArray();
    await using var server = new LocalServer(payload);
    await server.StartAsync();

    var stateItem = NewItem("https://user:secret@example.test/file?signature=keep", root, "state.bin");
    stateItem.Cookies = new List<BridgeCookie> { new() { Name = "session", Value = "private-cookie", Domain = "example.test", Path = "/" } };
    string stateJson = System.Text.Json.JsonSerializer.Serialize(stateItem);
    Check("persisted download state strips URL credentials and omits browser cookies", !stateJson.Contains("user:secret") && !stateJson.Contains("private-cookie") && stateJson.Contains("signature=keep"));

    var ranged = NewItem(server.Url("/range"), root, "range.bin");
    await DownloadEngine.ProbeAsync(ranged, CancellationToken.None);
    await DownloadEngine.RunAsync(ranged, 1, CancellationToken.None);
    Check("loopback range download writes the exact response bytes", File.ReadAllBytes(ranged.SavePath).SequenceEqual(payload));

    var redirected = NewItem(server.Url("/redirect"), root, "redirect.bin");
    await DownloadEngine.ProbeAsync(redirected, CancellationToken.None);
    await DownloadEngine.RunAsync(redirected, 1, CancellationToken.None);
    Check("same-origin redirect keeps the download working", File.ReadAllBytes(redirected.SavePath).SequenceEqual(payload));

    var retry = NewItem(server.Url("/retry"), root, "retry.bin");
    await DownloadEngine.ProbeAsync(retry, CancellationToken.None);
    await DownloadEngine.RunAsync(retry, 1, CancellationToken.None);
    Check("temporary 503 response is retried and the final file is exact", File.ReadAllBytes(retry.SavePath).SequenceEqual(payload));

    var error = NewItem(server.Url("/error"), root, "error.bin");
    bool rejectedServerError = false;
    try { await DownloadEngine.ProbeAsync(error, CancellationToken.None); }
    catch (HttpRequestException) { rejectedServerError = true; }
    Check("server errors are surfaced without creating a completed file", rejectedServerError && !File.Exists(error.SavePath));

    var mismatch = NewItem(server.Url("/mismatch"), root, "mismatch.bin");
    await DownloadEngine.ProbeAsync(mismatch, CancellationToken.None);
    bool rejectedRange = false;
    try { await DownloadEngine.RunAsync(mismatch, 1, CancellationToken.None); }
    catch (InvalidDataException) { rejectedRange = true; }
    Check("mismatched Content-Range is rejected and never promoted to a final file", rejectedRange && !File.Exists(mismatch.SavePath));

    var zero = NewItem(server.Url("/zero"), root, "zero.bin");
    await DownloadEngine.ProbeAsync(zero, CancellationToken.None);
    await DownloadEngine.RunAsync(zero, 1, CancellationToken.None);
    Check("zero-byte response produces a zero-byte completed file", File.Exists(zero.SavePath) && new FileInfo(zero.SavePath).Length == 0);

    var noRange = NewItem(server.Url("/no-range"), root, "no-range.bin");
    await DownloadEngine.ProbeAsync(noRange, CancellationToken.None);
    await DownloadEngine.RunAsync(noRange, 4, CancellationToken.None);
    Check("server without range support downloads as one exact stream", !noRange.SupportsRange && File.ReadAllBytes(noRange.SavePath).SequenceEqual(payload));

    var cookieA = NewItem(server.Url("/cookie"), root, "cookie-a.txt");
    cookieA.Cookies = new List<BridgeCookie> { new() { Name = "session", Value = "A", Domain = "127.0.0.1", Path = "/" } };
    var cookieB = NewItem(server.Url("/cookie"), root, "cookie-b.txt");
    cookieB.Cookies = new List<BridgeCookie> { new() { Name = "session", Value = "B", Domain = "127.0.0.1", Path = "/" } };
    await Task.WhenAll(
        DownloadEngine.ProbeAsync(cookieA, CancellationToken.None),
        DownloadEngine.ProbeAsync(cookieB, CancellationToken.None));
    await Task.WhenAll(
        DownloadEngine.RunAsync(cookieA, 1, CancellationToken.None),
        DownloadEngine.RunAsync(cookieB, 1, CancellationToken.None));
    Check("parallel downloads to the same host retain their own browser session", File.ReadAllText(cookieA.SavePath) == "A" && File.ReadAllText(cookieB.SavePath) == "B");
    using (var scopedClient = DownloadEngine.CreateCookieClient(server.Url("/redirect-host-cookie"), new[]
    {
        new BridgeCookie { Name = "session", Value = "private", Domain = "127.0.0.1", Path = "/" }
    }))
    using (var redirectedCookie = await scopedClient.GetAsync(server.Url("/redirect-host-cookie")))
    {
        string value = await redirectedCookie.Content.ReadAsStringAsync();
        Check("redirect to a sibling host does not receive a host-only extension cookie", value == "missing");
    }

    var immediateListing = await RemoteLister.ListDirectoryAsync(new Uri(server.Url("/index/")), null, CancellationToken.None);
    Check("HTTP directory browsing lists only immediate children and filters traversal links",
        immediateListing.Children.Count == 2 && immediateListing.Children.Any(x => x.IsDir && x.Name == "sub") &&
        immediateListing.Children.Any(x => !x.IsDir && x.Name == "file one.txt") && server.SubdirectoryRequests == 0);
    var recursiveListing = await RemoteLister.ScanAsync(new Uri(server.Url("/index/")), null, CancellationToken.None);
    Check("recursive enumeration occurs only after explicit scan and finds nested files",
        recursiveListing.Children.Single(x => x.IsDir).Children.Any(x => x.Name == "deep.mp4") && server.SubdirectoryRequests == 1);
    bool rejectedPage = false;
    try { await RemoteLister.ListDirectoryAsync(new Uri(server.Url("/not-index/")), null, CancellationToken.None); }
    catch (InvalidDataException) { rejectedPage = true; }
    Check("unsupported HTTP pages are not silently treated as directory indexes", rejectedPage);
    bool boundedListing = false;
    try { await RemoteLister.ListDirectoryAsync(new Uri(server.Url("/index/huge/")), null, CancellationToken.None); }
    catch (InvalidDataException) { boundedListing = true; }
    Check("HTTP directory listings enforce the immediate-entry cap", boundedListing);
    bool boundedPage = false;
    try { await RemoteLister.ListDirectoryAsync(new Uri(server.Url("/index/oversize/")), null, CancellationToken.None); }
    catch (InvalidDataException) { boundedPage = true; }
    Check("HTTP directory browsing rejects oversized chunked pages", boundedPage);

    var concurrentItems = Enumerable.Range(0, 4)
        .Select(i => NewItem(server.Url("/slow-job/" + i), root, "concurrent-" + i + ".bin"))
        .ToList();
    foreach (var item in concurrentItems) await DownloadEngine.ProbeAsync(item, CancellationToken.None);
    server.ResetSlowMetrics();
    await RunQueueAsync(concurrentItems, 4);
    Check("four independent slow transfers run concurrently at cap four", server.MaxSlowConcurrent >= 4 && concurrentItems.All(i => File.Exists(i.SavePath)));

    var serializedItems = Enumerable.Range(0, 3)
        .Select(i => NewItem(server.Url("/slow-job/serial-" + i), root, "serial-" + i + ".bin"))
        .ToList();
    foreach (var item in serializedItems) await DownloadEngine.ProbeAsync(item, CancellationToken.None);
    server.ResetSlowMetrics();
    await RunQueueAsync(serializedItems, 1);
    Check("cap one serializes slow transfers and releases each slot", server.MaxSlowConcurrent == 1 && serializedItems.All(i => File.Exists(i.SavePath)));

    string barrierDir = Path.Combine(root, "process-barrier");
    Directory.CreateDirectory(barrierDir);
    var processJobs = Enumerable.Range(0, 4).Select(i => YtDlpRunner.RunAsync(processHost,
        new[] { "--wait-for-release", barrierDir, "child-" + i }, _ => { }, _ => { }, CancellationToken.None)).ToArray();
    var barrierTimer = Stopwatch.StartNew();
    while (Directory.GetFiles(barrierDir, "child-*").Length < 4 && barrierTimer.Elapsed < TimeSpan.FromSeconds(8))
        await Task.Delay(20);
    bool fourProcessesStarted = Directory.GetFiles(barrierDir, "child-*").Length == 4;
    File.WriteAllText(Path.Combine(barrierDir, "release"), "go");
    int[] processResults = await Task.WhenAll(processJobs);
    Check("four yt-dlp child processes can occupy the process gate concurrently", fourProcessesStarted && processResults.All(x => x == 0));

    var resume = NewItem(server.Url("/slow"), root, "resume.bin");
    await DownloadEngine.ProbeAsync(resume, CancellationToken.None);
    using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(180)))
    {
        try { await DownloadEngine.RunAsync(resume, 1, cancel.Token); }
        catch (OperationCanceledException) { }
    }
    long partial = resume.Downloaded;
    bool keptTemp = File.Exists(resume.TempPath) && !File.Exists(resume.SavePath);
    await DownloadEngine.RunAsync(resume, 1, CancellationToken.None);
    Check("cancelled ranged download keeps a temp file and resumes exact segment progress", partial > 0 && keptTemp && File.ReadAllBytes(resume.SavePath).SequenceEqual(payload));
}
catch (Exception ex)
{
    Console.WriteLine("FAIL  integration harness: " + AppLog.Redact(ex.ToString()));
    failures++;
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

Console.WriteLine($"\n{failures} integration failure(s)");
return failures == 0 ? 0 : 1;

static DownloadItem NewItem(string url, string folder, string name) => new()
{
    Url = url,
    Folder = folder,
    FileName = name
};

static async Task RunQueueAsync(List<DownloadItem> queued, int maximum)
{
    var running = new List<Task>();
    while (queued.Count > 0 || running.Count > 0)
    {
        int slots = DownloadQueuePolicy.AvailableSlots(running.Count, maximum);
        while (slots-- > 0 && queued.Count > 0)
        {
            var item = queued[0];
            queued.RemoveAt(0);
            running.Add(DownloadEngine.RunAsync(item, 1, CancellationToken.None));
        }
        if (running.Count == 0) continue;
        Task completed = await Task.WhenAny(running);
        running.Remove(completed);
        await completed;
    }
}

sealed class LocalServer : IAsyncDisposable
{
    readonly byte[] payload;
    readonly HttpListener listener = new();
    int retryRequests;
    int activeSlow;
    int maxSlow;
    int subdirectoryRequests;
    Task? loop;
    CancellationTokenSource stopped = new();
    public int Port { get; private set; }
    public int MaxSlowConcurrent => Volatile.Read(ref maxSlow);
    public int SubdirectoryRequests => Volatile.Read(ref subdirectoryRequests);
    public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

    public LocalServer(byte[] payload) => this.payload = payload;

    public Task StartAsync()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        listener.Prefixes.Add($"http://localhost:{Port}/");
        listener.Start();
        loop = Task.Run(ServeAsync);
        return Task.CompletedTask;
    }

    async Task ServeAsync()
    {
        while (!stopped.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(async () =>
            {
                try { await HandleAsync(context); }
                catch { try { context.Response.Abort(); } catch { } }
            });
        }
    }

    async Task HandleAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;
        string path = req.Url?.AbsolutePath ?? "/";
        bool trackedSlow = path.StartsWith("/slow-job/", StringComparison.Ordinal) && req.Headers["Range"] != "bytes=0-0";
        if (trackedSlow)
        {
            int active = Interlocked.Increment(ref activeSlow);
            int observed;
            while (active > (observed = Volatile.Read(ref maxSlow)) &&
                   Interlocked.CompareExchange(ref maxSlow, active, observed) != observed) { }
        }
        try
        {
            await HandleCoreAsync(context, path);
        }
        finally
        {
            if (trackedSlow) Interlocked.Decrement(ref activeSlow);
        }
    }

    async Task HandleCoreAsync(HttpListenerContext context, string path)
    {
        var req = context.Request;
        var res = context.Response;
        if (path == "/index/" || path == "/index/sub/" || path == "/not-index/" || path == "/index/huge/" || path == "/index/oversize/")
        {
            if (path == "/index/sub/") Interlocked.Increment(ref subdirectoryRequests);
            string page = path switch
            {
                "/index/" => "<html><title>Index of /index/</title><h1>Index of /index/</h1><a href='../escape.txt'>bad</a><a href='encoded%2Fseparator.txt'>bad2</a><a href='sub/'>sub/</a><a href='file%20one.txt'>file one.txt</a></html>",
                "/index/sub/" => "<html><title>Index of /index/sub/</title><a href='deep.mp4'>deep.mp4</a></html>",
                "/not-index/" => "<html><title>Welcome</title><a href='section/'>section</a><a href='about'>about</a></html>",
                "/index/oversize/" => "<html><title>Index of /index/oversize/</title><body>" + new string('x', 2 * 1024 * 1024 + 128) + "</body></html>",
                _ => "<html><title>Index of /index/huge/</title>" + string.Concat(Enumerable.Range(0, 1001).Select(i => "<a href='f" + i + ".bin'>f</a>")) + "</html>"
            };
            byte[] html = Encoding.UTF8.GetBytes(page);
            res.ContentType = "text/html; charset=utf-8";
            if (path == "/index/oversize/") res.SendChunked = true;
            else res.ContentLength64 = html.Length;
            await res.OutputStream.WriteAsync(html);
            res.Close();
            return;
        }
        if (path == "/redirect")
        {
            res.StatusCode = 302;
            res.RedirectLocation = "/range";
            res.ContentLength64 = 0;
            res.Close();
            return;
        }
        if (path == "/redirect-host-cookie")
        {
            res.StatusCode = 302;
            res.RedirectLocation = "http://localhost:" + Port + "/cookie";
            res.ContentLength64 = 0;
            res.Close();
            return;
        }
        if (path == "/zero")
        {
            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentLength64 = 0;
            res.Close();
            return;
        }
        if (path == "/error")
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            res.ContentLength64 = 0;
            res.Close();
            return;
        }
        if (path == "/cookie")
        {
            string body = req.Cookies["session"]?.Value ?? "missing";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body);
            res.ContentType = "text/plain";
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
            res.Close();
            return;
        }

        string? rangeHeader = req.Headers["Range"];
        if (path == "/retry" && Interlocked.Increment(ref retryRequests) == 2)
        {
            res.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            res.ContentLength64 = 0;
            res.Close();
            return;
        }
        if (rangeHeader == null || path == "/no-range")
        {
            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentLength64 = payload.Length;
            await WriteBytesAsync(res, payload, 0, payload.Length, slow: path == "/slow" || path.StartsWith("/slow-job/", StringComparison.Ordinal));
            res.Close();
            return;
        }

        var parts = rangeHeader.Replace("bytes=", "", StringComparison.OrdinalIgnoreCase).Split('-', 2);
        long start = long.Parse(parts[0]);
        long end = Math.Min(payload.Length - 1, long.Parse(parts[1]));
        if (path == "/mismatch" && (start != 0 || end != 0)) end--;
        res.StatusCode = (int)HttpStatusCode.PartialContent;
        res.Headers[HttpResponseHeader.ContentRange] = $"bytes {start}-{end}/{payload.Length}";
        res.ContentLength64 = end - start + 1;
        await WriteBytesAsync(res, payload, (int)start, (int)(end - start + 1), slow: (path == "/slow" || path.StartsWith("/slow-job/", StringComparison.Ordinal)) && end > 0);
        res.Close();
    }

    public void ResetSlowMetrics()
    {
        Volatile.Write(ref activeSlow, 0);
        Volatile.Write(ref maxSlow, 0);
    }

    static async Task WriteBytesAsync(HttpListenerResponse response, byte[] data, int offset, int count, bool slow)
    {
        const int chunk = 32 * 1024;
        int end = offset + count;
        for (int at = offset; at < end;)
        {
            int n = Math.Min(chunk, end - at);
            await response.OutputStream.WriteAsync(data.AsMemory(at, n));
            await response.OutputStream.FlushAsync();
            at += n;
            if (slow && at < end) await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        stopped.Cancel();
        try { listener.Close(); } catch { }
        if (loop != null) try { await loop; } catch { }
        stopped.Dispose();
    }
}
