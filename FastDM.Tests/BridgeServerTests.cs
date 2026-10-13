using System.Net;
using System.Text;
using System.Text.Json;

namespace FastDM.Tests
{
    // Starts the real BridgeServer on a loopback port and talks to it over HTTP like the extension does.
    static class BridgeServerTests
    {
        const string A = "chrome-extension://abcdefghijklmnopabcdefghijklmnop";
        const string B = "chrome-extension://ponmlkjihgfedcbaponmlkjihgfedcba";
        const string C = "chrome-extension://cccccccccccccccccccccccccccccccc";
        const string D = "chrome-extension://dddddddddddddddddddddddddddddddd";

        sealed class FakeHost : IBridgeHost
        {
            public FakeHost(AppSettings s) { BridgeSettings = s; }
            public AppSettings BridgeSettings { get; }
            public bool Answer;
            public int Asks, Added, Saves;
            public string? LastAddedUrl, LastAddedMode;
            public Task<bool> AskPairAsync(string origin) { Asks++; return Task.FromResult(Answer); }
            public void ExternalAdd(BridgeAddRequest add) { Added++; LastAddedUrl = add.Url; LastAddedMode = add.Mode; }
            public List<BridgeTaskInfo> GetTasks() => new List<BridgeTaskInfo>();
            public bool TaskAction(string id, string action) => false;
            public void SaveSettings() { Saves++; }
        }

        public static async Task RunAsync()
        {
            T.Section("BridgeServer over HTTP");

            var settings = new AppSettings();
            var host = new FakeHost(settings);
            using var server = new BridgeServer(host);
            if (!server.Start())
            {
                T.Check("server starts (ports 17432-17436 must be free; close FastDM and other copies of this test)", false);
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            string baseUrl = "http://127.0.0.1:" + server.Port;

            async Task<(HttpStatusCode Code, JsonElement Json)> Call(string method, string path, string? origin, string? token = null, string? body = null, string? hostHeader = null)
            {
                var req = new HttpRequestMessage(new HttpMethod(method), baseUrl + path);
                if (origin != null) req.Headers.TryAddWithoutValidation("Origin", origin);
                if (token != null) req.Headers.TryAddWithoutValidation("X-FastDM-Token", token);
                if (hostHeader != null) req.Headers.Host = hostHeader;
                if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var r = await http.SendAsync(req);
                string txt = await r.Content.ReadAsStringAsync();
                JsonElement el = default;
                if (txt.Length > 0)
                {
                    try { el = JsonDocument.Parse(txt).RootElement.Clone(); }
                    catch (JsonException) { /* the OS HTTP layer can answer with plain HTML */ }
                }
                return (r.StatusCode, el);
            }

            static bool Paired(JsonElement e) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("paired", out var p) && p.GetBoolean();
            static string Err(JsonElement e) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("error", out var p) ? p.GetString() ?? "" : "";

            // ---- who may talk to the server at all ----
            var (c0, j0) = await Call("GET", "/v1/ping", null);
            T.Check("no Origin header -> 403 origin_not_allowed", c0 == HttpStatusCode.Forbidden && Err(j0) == "origin_not_allowed");
            T.Check("web page Origin -> 403", (await Call("GET", "/v1/ping", "http://evil.com")).Code == HttpStatusCode.Forbidden);
            T.Check("extension-looking Origin with a path -> 403", (await Call("GET", "/v1/ping", A + "/evil")).Code == HttpStatusCode.Forbidden);
            var (ch, jh) = await Call("GET", "/v1/ping", A, hostHeader: "evil.com");
            T.Check("wrong Host header (DNS rebinding) is refused", ch != HttpStatusCode.OK && !Paired(jh));
            var (c2, j2) = await Call("GET", "/v1/ping", A);
            T.Check("unknown extension can ping, paired=false", c2 == HttpStatusCode.OK && !Paired(j2) && j2.GetProperty("app").GetString() == "FastDM");
            T.Check("no token -> /v1/tasks 401", (await Call("GET", "/v1/tasks", A)).Code == HttpStatusCode.Unauthorized);
            T.Check("no token -> /v1/add 401", (await Call("POST", "/v1/add", A, body: "{\"url\":\"https://a.test/x\"}")).Code == HttpStatusCode.Unauthorized);

            // ---- pairing ----
            host.Answer = true;
            var (c3, j3) = await Call("POST", "/v1/pair", A);
            string tokenA = c3 == HttpStatusCode.OK ? j3.GetProperty("token").GetString()! : "";
            T.Check("pair A approved -> token", tokenA.Length > 20);
            T.Check("approved extension's origin is remembered", settings.BridgeTokenOrigins.Values.Contains(A.ToLowerInvariant()));
            T.Check("ping from A with its token -> paired", Paired((await Call("GET", "/v1/ping", A, tokenA)).Json));
            T.Check("ping from B with A's token -> not paired", !Paired((await Call("GET", "/v1/ping", B, tokenA)).Json));

            var (c6, _) = await Call("POST", "/v1/add", B, tokenA, "{\"url\":\"https://a.test/a.zip\"}");
            T.Check("add from B with A's token -> 401, nothing added", c6 == HttpStatusCode.Unauthorized && host.Added == 0);
            T.Check("tasks from B with A's token -> 401", (await Call("GET", "/v1/tasks", B, tokenA)).Code == HttpStatusCode.Unauthorized);

            var (c7, _) = await Call("POST", "/v1/add", A, tokenA, "{\"url\":\"https://a.test/a.zip\",\"mode\":\"download\"}");
            T.Check("add from A -> 200 and reaches the app", c7 == HttpStatusCode.OK && host.Added == 1 && host.LastAddedUrl == "https://a.test/a.zip" && host.LastAddedMode == "download");

            var (c7b, j7b) = await Call("POST", "/v1/add", A, tokenA, "{\"url\":\"javascript:alert(1)\"}");
            T.Check("add with a javascript: link -> 400 bad_url", c7b == HttpStatusCode.BadRequest && Err(j7b) == "bad_url" && host.Added == 1);
            var (c7c, j7c) = await Call("POST", "/v1/add", A, tokenA, "{ not json");
            T.Check("add with broken JSON -> 400 bad_json", c7c == HttpStatusCode.BadRequest && Err(j7c) == "bad_json");
            T.Check("tasks from A -> 200", (await Call("GET", "/v1/tasks", A, tokenA)).Code == HttpStatusCode.OK);

            // ---- refused extension cannot spam dialogs ----
            host.Answer = false;
            int asks = host.Asks;
            var (c8, j8) = await Call("POST", "/v1/pair", B);
            T.Check("pair B refused -> 403 pairing_denied (one dialog)", c8 == HttpStatusCode.Forbidden && Err(j8) == "pairing_denied" && host.Asks == asks + 1);
            host.Answer = true;
            var (c9, j9) = await Call("POST", "/v1/pair", B);
            T.Check("B asks again at once -> denied with NO new dialog", c9 == HttpStatusCode.Forbidden && Err(j9) == "pairing_denied" && host.Asks == asks + 1);
            var (c9b, _) = await Call("POST", "/v1/pair", C);
            T.Check("another extension is not blocked by B's cooldown", c9b == HttpStatusCode.OK && host.Asks == asks + 2);

            // ---- pairing again, forgetting ----
            var (_, j10) = await Call("POST", "/v1/pair", A);
            string tokenA2 = j10.GetProperty("token").GetString()!;
            T.Check("A pairs again -> old token stops working, new one works",
                !Paired((await Call("GET", "/v1/ping", A, tokenA)).Json) && Paired((await Call("GET", "/v1/ping", A, tokenA2)).Json));

            // ---- token paired by an older app version ----
            string legacyTok = BridgeAuth.NewToken();
            settings.BridgeTokens.Add(BridgeAuth.Hash(legacyTok));
            int saves = host.Saves;
            T.Check("older token accepted, origin learned and saved",
                Paired((await Call("GET", "/v1/ping", D, legacyTok)).Json) && host.Saves == saves + 1);
            T.Check("older token then rejected from another extension", !Paired((await Call("GET", "/v1/ping", B, legacyTok)).Json));

            BridgeAuth.ForgetAll(settings);
            T.Check("after 'Forget paired browsers' -> not paired", !Paired((await Call("GET", "/v1/ping", A, tokenA2)).Json));
        }
    }
}
