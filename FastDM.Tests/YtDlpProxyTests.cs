using System.Diagnostics;
using FastDM;

namespace FastDM.Tests
{
    static class YtDlpProxyTests
    {
        public static void Run()
        {
            T.Section("yt-dlp proxy credential handling");

            const string user = "proxy user";
            const string password = "p@ss:word";
            var start = new ProcessStartInfo("yt-dlp.exe");
            YtDlpProxy.ApplyEnvironment(start, ProxyMode.Manual, ProxyKind.Http,
                "proxy.example.test", 8080, user, password);
            var args = YtDlpProxy.BuildArguments(ProxyMode.Manual, ProxyKind.Http,
                "proxy.example.test", 8080, user);
            string? proxy = start.Environment["https_proxy"];
            var parsed = proxy == null ? null : new Uri(proxy);

            T.Check("authenticated proxy credentials are encoded in child environment, not argv",
                args.Count == 0 && parsed != null &&
                Uri.UnescapeDataString(parsed.UserInfo.Split(':', 2)[0]) == user &&
                Uri.UnescapeDataString(parsed.UserInfo.Split(':', 2)[1]) == password &&
                !string.Join(" ", args).Contains(password));

            var anonymous = YtDlpProxy.BuildArguments(ProxyMode.Manual, ProxyKind.Socks5,
                "proxy.example.test", 1080, "");
            T.Check("unauthenticated proxy remains an explicit supported yt-dlp argument",
                anonymous.SequenceEqual(new[] { "--proxy", "socks5://proxy.example.test:1080/" }));

            var direct = YtDlpProxy.BuildArguments(ProxyMode.None, ProxyKind.Http,
                "", 0, "");
            T.Check("disabled proxy explicitly overrides inherited proxy environment", direct.Count == 2 && direct[0] == "--proxy" && direct[1] == "");

            bool invalidRejected = false;
            try { YtDlpProxy.BuildArguments(ProxyMode.Manual, ProxyKind.Http, "proxy.example.test/path", 8080, ""); }
            catch (ArgumentException) { invalidRejected = true; }
            T.Check("proxy host input cannot inject a path or extra URL fields", invalidRejected);

            var urlInput = YtDlpUrlInput.Create("https://video.example.test/watch?v=signed-token");
            T.Check("yt-dlp video URLs travel over redirected stdin, not process arguments",
                urlInput.Arguments.SequenceEqual(new[] { "--batch-file", "-" }) &&
                !urlInput.Arguments.Contains("https://video.example.test/watch?v=signed-token") &&
                urlInput.StandardInput == "https://video.example.test/watch?v=signed-token\n");

            string dir = Path.Combine(Path.GetTempPath(), "fastdm-tool-path-" + Guid.NewGuid().ToString("N"));
            string allowed = Path.Combine(dir, "app-tools");
            string windows = Path.Combine(dir, "system");
            string unrelated = Path.Combine(dir, "untrusted-path");
            Directory.CreateDirectory(allowed);
            Directory.CreateDirectory(windows);
            try
            {
                string restrictedPath = YtDlpProcessEnvironment.BuildRestrictedPath(new[] { allowed, windows });
                T.Check("yt-dlp child PATH contains only explicitly allowed directories", restrictedPath.Contains(allowed) && restrictedPath.Contains(windows) && !restrictedPath.Contains(unrelated));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
