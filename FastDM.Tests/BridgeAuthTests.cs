using System.Text.Json;

namespace FastDM.Tests
{
    static class BridgeAuthTests
    {
        const string A = "chrome-extension://abcdefghijklmnopabcdefghijklmnop";
        const string B = "chrome-extension://ponmlkjihgfedcbaponmlkjihgfedcba";
        const string C = "chrome-extension://cccccccccccccccccccccccccccccccc";
        const string D = "chrome-extension://dddddddddddddddddddddddddddddddd";

        public static void Run()
        {
            Origins();
            Binding();
            Urls();
            Cookies();
            Headers();
        }

        static void Origins()
        {
            T.Section("Extension origin shape");
            T.Check("chrome id ok", BridgeAuth.IsExtensionOrigin(A));
            T.Check("moz uuid ok", BridgeAuth.IsExtensionOrigin("moz-extension://3f2b1c9e-1111-4a2b-9c3d-1234567890ab"));
            T.Check("uppercase scheme ok", BridgeAuth.IsExtensionOrigin("CHROME-EXTENSION://abcdefghijklmnopabcdefghijklmnop"));
            T.Check("bare scheme rejected", !BridgeAuth.IsExtensionOrigin("chrome-extension://"));
            T.Check("short id rejected", !BridgeAuth.IsExtensionOrigin("chrome-extension://abc"));
            T.Check("path rejected", !BridgeAuth.IsExtensionOrigin(A + "/evil"));
            T.Check("port rejected", !BridgeAuth.IsExtensionOrigin(A + ":8080"));
            T.Check("trailing newline rejected", !BridgeAuth.IsExtensionOrigin(A + "\n"));
            T.Check("space rejected", !BridgeAuth.IsExtensionOrigin(A + " x"));
            T.Check("web page rejected", !BridgeAuth.IsExtensionOrigin("http://evil.com"));
            T.Check("null / empty rejected", !BridgeAuth.IsExtensionOrigin(null) && !BridgeAuth.IsExtensionOrigin(""));
        }

        static void Binding()
        {
            T.Section("Token bound to the approved extension");
            var s = new AppSettings();
            string t1 = BridgeAuth.NewToken();
            BridgeAuth.AddToken(s, t1, A);

            T.Check("same origin valid", BridgeAuth.Evaluate(s, t1, A, out var l1) == BridgeTokenState.Valid && !l1);
            T.Check("origin compare ignores case", BridgeAuth.Evaluate(s, t1, A.ToUpperInvariant(), out _) == BridgeTokenState.Valid);
            T.Check("other extension -> WrongOrigin", BridgeAuth.Evaluate(s, t1, B, out _) == BridgeTokenState.WrongOrigin);
            T.Check("unknown token -> None", BridgeAuth.Evaluate(s, "nope", A, out _) == BridgeTokenState.None);
            T.Check("null token -> None", BridgeAuth.Evaluate(s, null, A, out _) == BridgeTokenState.None);
            T.Check("empty origin -> WrongOrigin", BridgeAuth.Evaluate(s, t1, "", out _) == BridgeTokenState.WrongOrigin);
            T.Check("token over 200 chars -> None", BridgeAuth.Evaluate(s, new string('x', 201), A, out _) == BridgeTokenState.None);
            T.Check("only the hash is stored, not the token", !s.BridgeTokens.Contains(t1) && s.BridgeTokens.Contains(BridgeAuth.Hash(t1)));

            string t2 = BridgeAuth.NewToken();
            BridgeAuth.AddToken(s, t2, A);
            T.Check("pairing again replaces the old token", BridgeAuth.Evaluate(s, t1, A, out _) == BridgeTokenState.None
                                                       && BridgeAuth.Evaluate(s, t2, A, out _) == BridgeTokenState.Valid);
            T.Check("no pile-up for the same extension", s.BridgeTokens.Count == 1 && s.BridgeTokenOrigins.Count == 1);

            string legacy = BridgeAuth.NewToken();
            s.BridgeTokens.Add(BridgeAuth.Hash(legacy));           // paired by an older app version: no origin saved
            T.Check("legacy token: accepted once and learned", BridgeAuth.Evaluate(s, legacy, C, out var learned) == BridgeTokenState.Valid && learned);
            T.Check("legacy token: not learned twice", BridgeAuth.Evaluate(s, legacy, C, out var l2) == BridgeTokenState.Valid && !l2);
            T.Check("legacy token: other extension rejected afterwards", BridgeAuth.Evaluate(s, legacy, D, out _) == BridgeTokenState.WrongOrigin);

            var many = new AppSettings();
            for (int i = 0; i < 15; i++)
                BridgeAuth.AddToken(many, BridgeAuth.NewToken(), "chrome-extension://" + new string((char)('a' + i), 32));
            T.Check("at most MaxPairedClients tokens", many.BridgeTokens.Count == BridgeAuth.MaxPairedClients);
            T.Check("origins trimmed together with tokens",
                many.BridgeTokenOrigins.Count == many.BridgeTokens.Count && many.BridgeTokenOrigins.Keys.All(k => many.BridgeTokens.Contains(k)));

            BridgeAuth.ForgetAll(s);
            T.Check("Forget all clears tokens and origins",
                s.BridgeTokens.Count == 0 && s.BridgeTokenOrigins.Count == 0 && BridgeAuth.Evaluate(s, t2, A, out _) == BridgeTokenState.None);

            var loaded = JsonSerializer.Deserialize<AppSettings>("{\"BridgeTokens\":[\"AB\"]}")!;
            T.Check("old state.json (no origin map) loads with an empty map", loaded.BridgeTokenOrigins != null && loaded.BridgeTokenOrigins.Count == 0);
        }

        static void Urls()
        {
            T.Section("Allowed link types");
            foreach (var ok in new[] { "https://a.test/x.zip", "http://a.test/x", "ftp://a.test/x", "sftp://a.test/x" })
                T.Check("allowed: " + ok, BridgeAuth.IsAllowedUrl(ok));

            foreach (var bad in new[] { "javascript:alert(1)", "file:///c:/windows/system32/calc.exe", "fastdm://open", "data:text/html,x", "chrome://settings", "not a url", "", "   " })
                T.Check("blocked: '" + bad + "'", !BridgeAuth.IsAllowedUrl(bad));

            T.Check("blocked: URL over 8192 chars", !BridgeAuth.IsAllowedUrl("https://a.test/" + new string('a', 8200)));
        }

        static BridgeCookie Ck(string name, string domain, string value = "v", string path = "/") =>
            new BridgeCookie { Name = name, Value = value, Domain = domain, Path = path, Secure = true };

        static void Cookies()
        {
            T.Section("Cookie isolation (cookies only reach the site they belong to)");
            string url = "https://www.example.com/dl/file.zip";

            var r = BridgeAuth.CleanCookies(new List<BridgeCookie>
            {
                Ck("ok1", ".example.com"),
                Ck("ok2", "www.example.com"),
                Ck("evil", "evil.com"),
                Ck("lookalike", "notexample.com"),
                Ck("sibling", "api.example.com"),
                Ck("deeper", "x.www.example.com"),
            }, url);
            var names = r?.Select(c => c.Name).ToHashSet() ?? new HashSet<string>();
            T.Check("matching domains kept", names.Contains("ok1") && names.Contains("ok2"));
            T.Check("other site dropped", !names.Contains("evil"));
            T.Check("look-alike domain dropped", !names.Contains("lookalike"));
            T.Check("sibling subdomain dropped", !names.Contains("sibling"));
            T.Check("deeper subdomain dropped", !names.Contains("deeper"));
            T.Check("leading dot (domain cookie) preserved", r!.First(c => c.Name == "ok1").Domain == ".example.com");
            T.Check("no dot (host-only cookie) preserved", r!.First(c => c.Name == "ok2").Domain == "www.example.com");

            T.Check("only foreign cookies -> null", BridgeAuth.CleanCookies(new List<BridgeCookie> { Ck("e", "evil.com") }, url) == null);
            T.Check("ftp link -> null", BridgeAuth.CleanCookies(new List<BridgeCookie> { Ck("a", "example.com") }, "ftp://example.com/f") == null);
            T.Check("sftp link -> null", BridgeAuth.CleanCookies(new List<BridgeCookie> { Ck("a", "example.com") }, "sftp://example.com/f") == null);
            T.Check("null list -> null", BridgeAuth.CleanCookies(null, url) == null);

            var odd = BridgeAuth.CleanCookies(new List<BridgeCookie>
            {
                Ck("a;b", "example.com"), Ck("a b", "example.com"), Ck("a=b", "example.com"),
                Ck("ctl", "example.com", value: "x\r\nSet-Cookie: y=1"), Ck("good", "example.com", path: "no-slash"),
            }, url);
            T.Check("bad names / control characters in value dropped", odd != null && odd.Count == 1 && odd[0].Name == "good");
            T.Check("relative path normalised to /", odd![0].Path == "/");

            var lots = Enumerable.Range(0, 150).Select(i => Ck("c" + i, "example.com")).ToList();
            T.Check("at most MaxCookies cookies", BridgeAuth.CleanCookies(lots, url)!.Count == BridgeAuth.MaxCookies);
        }

        static void Headers()
        {
            T.Section("Referer / User-Agent / mode");
            T.Check("referer https kept", BridgeAuth.CleanReferer("https://a.test/page") == "https://a.test/page");
            T.Check("referer javascript: dropped", BridgeAuth.CleanReferer("javascript:alert(1)") == null);
            T.Check("referer with newline dropped", BridgeAuth.CleanReferer("https://a.test/\r\nX: y") == null);
            T.Check("referer over 2048 dropped", BridgeAuth.CleanReferer("https://a.test/" + new string('a', 2100)) == null);
            T.Check("user-agent kept", BridgeAuth.CleanUserAgent("Mozilla/5.0 X") == "Mozilla/5.0 X");
            T.Check("user-agent with newline dropped", BridgeAuth.CleanUserAgent("UA\r\nX: y") == null);
            T.Check("user-agent over 512 dropped", BridgeAuth.CleanUserAgent(new string('u', 600)) == null);
            T.Check("mode download / playlist", BridgeAuth.NormalizeMode("DOWNLOAD") == "download" && BridgeAuth.NormalizeMode("playlist") == "playlist");
            T.Check("unknown mode -> open", BridgeAuth.NormalizeMode("whatever") == "open" && BridgeAuth.NormalizeMode("") == "open");
        }
    }
}
