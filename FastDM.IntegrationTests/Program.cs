using System.Net;
using System.Net.Sockets;
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

sealed class LocalServer : IAsyncDisposable
{
    readonly byte[] payload;
    readonly HttpListener listener = new();
    int retryRequests;
    Task? loop;
    CancellationTokenSource stopped = new();
    public int Port { get; private set; }
    public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

    public LocalServer(byte[] payload) => this.payload = payload;

    public Task StartAsync()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
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
            try { await HandleAsync(context); }
            catch { try { context.Response.Abort(); } catch { } }
        }
    }

    async Task HandleAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;
        string path = req.Url?.AbsolutePath ?? "/";
        if (path == "/redirect")
        {
            res.StatusCode = 302;
            res.RedirectLocation = "/range";
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
        if (rangeHeader == null)
        {
            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentLength64 = payload.Length;
            await WriteBytesAsync(res, payload, 0, payload.Length, slow: path == "/slow");
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
        await WriteBytesAsync(res, payload, (int)start, (int)(end - start + 1), slow: path == "/slow" && end > 0);
        res.Close();
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
