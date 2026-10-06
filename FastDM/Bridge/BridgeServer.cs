#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FastDM
{
    // অ্যাপের ভেতরের লোকাল সার্ভার: শুধু 127.0.0.1, শুধু ব্রাউজার এক্সটেনশনের Origin, প্রতি রিকোয়েস্টে টোকেন।
    //   GET  /v1/ping                      → অ্যাপ চলছে কি না (টোকেন ঐচ্ছিক, থাকলে paired: true/false)
    //   POST /v1/pair                      → অ্যাপে অনুমোদন ডায়ালগ, অনুমোদনে নতুন টোকেন
    //   POST /v1/add        {url,...}      → অ্যাপে Add ডায়ালগ খোলে (ইউজার নিশ্চিত করবে)
    //   GET  /v1/tasks                     → সাম্প্রতিক ডাউনলোডের অবস্থা
    //   POST /v1/tasks/{id}/{pause|resume}
    public sealed class BridgeServer : IDisposable
    {
        public const int ProtocolVersion = 1;
        public static readonly int[] Ports = { 17432, 17433, 17434, 17435, 17436 };

        const int MaxBody = 256 * 1024;
        const int AddLimitPerWindow = 30;
        static readonly TimeSpan AddWindow = TimeSpan.FromSeconds(10);
        static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(60);

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        readonly IBridgeHost host;
        readonly Queue<DateTime> addTimes = new Queue<DateTime>();
        HttpListener listener;
        CancellationTokenSource cts;
        int pairing;                              // একসাথে একটাই পেয়ারিং অনুরোধ

        public int Port { get; private set; }
        public bool Running => listener != null && listener.IsListening;

        public BridgeServer(IBridgeHost host) { this.host = host; }

        public bool Start()
        {
            foreach (int p in Ports)
            {
                try
                {
                    var l = new HttpListener();
                    l.Prefixes.Add("http://127.0.0.1:" + p + "/");
                    l.Start();
                    listener = l;
                    Port = p;
                    break;
                }
                catch { /* পোর্ট ব্যস্ত, পরেরটা */ }
            }
            if (listener == null) return false;

            cts = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(cts.Token));
            return true;
        }

        public void Dispose()
        {
            try { cts?.Cancel(); } catch { }
            try { listener?.Stop(); listener?.Close(); } catch { }
            listener = null;
        }

        async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => HandleAsync(ctx));
            }
        }

        // ---------- রিকোয়েস্ট ----------
        async Task HandleAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            try
            {
                // ১) Host চেক (DNS rebinding ঠেকাতে)
                string hostHdr = (req.Headers["Host"] ?? "").ToLowerInvariant();
                if (hostHdr != "127.0.0.1:" + Port && hostHdr != "localhost:" + Port)
                {
                    await Send(res, 403, new { error = "bad_host" });
                    return;
                }

                // ২) Origin চেক: শুধু ব্রাউজার এক্সটেনশন (ওয়েবপেজ Origin জাল করতে পারে না)
                string origin = req.Headers["Origin"];
                bool originOk = BridgeAuth.IsExtensionOrigin(origin);
                if (originOk)
                {
                    res.Headers["Access-Control-Allow-Origin"] = origin;
                    res.Headers["Vary"] = "Origin";
                    res.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-FastDM-Token";
                    res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                    res.Headers["Access-Control-Max-Age"] = "600";
                }
                if (req.HttpMethod == "OPTIONS")
                {
                    await Send(res, originOk ? 204 : 403, null);
                    return;
                }
                if (!originOk)
                {
                    await Send(res, 403, new { error = "origin_not_allowed" });
                    return;
                }

                string path = req.Url.AbsolutePath.TrimEnd('/');
                string token = req.Headers["X-FastDM-Token"];
                bool paired = BridgeAuth.IsValid(host.BridgeSettings, token);

                // ৩) রাউটিং
                if (req.HttpMethod == "GET" && path == "/v1/ping")
                {
                    await Send(res, 200, new
                    {
                        app = "FastDM",
                        version = UpdateChecker.Current.ToString(3),
                        protocol = ProtocolVersion,
                        paired
                    });
                    return;
                }

                if (req.HttpMethod == "POST" && path == "/v1/pair")
                {
                    await HandlePair(res, origin);
                    return;
                }

                // এর পরের সবকিছুতে টোকেন লাগবে
                if (!paired)
                {
                    await Send(res, 401, new { error = "unauthorized" });
                    return;
                }

                if (req.HttpMethod == "POST" && path == "/v1/add")
                {
                    await HandleAdd(req, res);
                    return;
                }

                if (req.HttpMethod == "GET" && path == "/v1/tasks")
                {
                    await Send(res, 200, host.GetTasks());
                    return;
                }

                if (req.HttpMethod == "POST" && path.StartsWith("/v1/tasks/"))
                {
                    var parts = path.Substring("/v1/tasks/".Length).Split('/');
                    if (parts.Length == 2 && (parts[1] == "pause" || parts[1] == "resume"))
                    {
                        bool ok = host.TaskAction(Uri.UnescapeDataString(parts[0]), parts[1]);
                        await Send(res, ok ? 200 : 404, new { ok });
                        return;
                    }
                }

                await Send(res, 404, new { error = "not_found" });
            }
            catch
            {
                try { await Send(res, 500, new { error = "server_error" }); } catch { }
            }
        }

        async Task HandlePair(HttpListenerResponse res, string origin)
        {
            if (Interlocked.CompareExchange(ref pairing, 1, 0) != 0)
            {
                await Send(res, 429, new { error = "pairing_busy" });
                return;
            }
            try
            {
                var ask = host.AskPairAsync(origin);
                var done = await Task.WhenAny(ask, Task.Delay(PairTimeout));
                if (done != ask) { await Send(res, 408, new { error = "pairing_timeout" }); return; }
                if (!await ask) { await Send(res, 403, new { error = "pairing_denied" }); return; }

                string token = BridgeAuth.NewToken();
                BridgeAuth.AddToken(host.BridgeSettings, token);
                host.SaveSettings();
                await Send(res, 200, new { token });
            }
            finally { Interlocked.Exchange(ref pairing, 0); }
        }

        async Task HandleAdd(HttpListenerRequest req, HttpListenerResponse res)
        {
            // রেট লিমিট
            lock (addTimes)
            {
                var now = DateTime.UtcNow;
                while (addTimes.Count > 0 && now - addTimes.Peek() > AddWindow) addTimes.Dequeue();
                if (addTimes.Count >= AddLimitPerWindow)
                {
                    _ = Send(res, 429, new { error = "rate_limited" });
                    return;
                }
                addTimes.Enqueue(now);
            }

            string body = await ReadBody(req);
            if (body == null) { await Send(res, 413, new { error = "body_too_large" }); return; }

            BridgeAddRequest add;
            try { add = JsonSerializer.Deserialize<BridgeAddRequest>(body, Json); }
            catch { await Send(res, 400, new { error = "bad_json" }); return; }

            if (add == null || !BridgeAuth.IsAllowedUrl(add.Url))
            {
                await Send(res, 400, new { error = "bad_url" });
                return;
            }

            host.ExternalAdd(add.Url.Trim(), add.Title);
            await Send(res, 200, new { ok = true, status = "opened" });
        }

        static async Task<string> ReadBody(HttpListenerRequest req)
        {
            if (req.ContentLength64 > MaxBody) return null;
            using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
            var sb = new StringBuilder();
            var buf = new char[4096];
            int n;
            while ((n = await sr.ReadAsync(buf, 0, buf.Length)) > 0)
            {
                sb.Append(buf, 0, n);
                if (sb.Length > MaxBody) return null;
            }
            return sb.ToString();
        }

        static async Task Send(HttpListenerResponse res, int status, object payload)
        {
            res.StatusCode = status;
            if (payload == null)
            {
                res.ContentLength64 = 0;
                res.Close();
                return;
            }
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = data.Length;
            await res.OutputStream.WriteAsync(data, 0, data.Length);
            res.Close();
        }
    }
}
