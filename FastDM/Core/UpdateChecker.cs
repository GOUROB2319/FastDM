using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FastDM
{
    // Store ভার্সন  -> Microsoft Store পেজ খোলে (আপডেট Store-ই দেয়)
    // পোর্টেবল ভার্সন -> পাবলিক GitHub releases রিপো থেকে সর্বশেষ ভার্সন চেক করে
    public static class UpdateChecker
    {
        public const string StoreId = "9P5ZP43GK911";

        // তোমার পাবলিক release রিপো (owner/name)। রিপোর আসল নাম অনুযায়ী মিলিয়ে নিও।
        public const string ReleasesRepo = "GOUROB2319/FastDM";

        public class Result
        {
            public bool Ok;
            public bool UpdateAvailable;
            public string LatestVersion = string.Empty;
            public string? Url;
            public string? Notes;
            public string? Error;
        }

        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FastDM-UpdateChecker/1.0");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        // ---------- বর্তমান ভার্সন ----------
        public static Version Current =>
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

        // ---------- MSIX প্যাকেজড কি না ----------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

        const int APPMODEL_ERROR_NO_PACKAGE = 15700;

        public static bool IsPackaged
        {
            get
            {
                try
                {
                    int len = 0;
                    return GetCurrentPackageFullName(ref len, null) != APPMODEL_ERROR_NO_PACKAGE;
                }
                catch { return false; }
            }
        }

        // ---------- Store ----------
        public static void OpenStore()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=" + StoreId)
                { UseShellExecute = true });
            }
            catch
            {
                OpenUrl("https://apps.microsoft.com/detail/" + StoreId);
            }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        // ---------- GitHub ----------
        public static async Task<Result> CheckGitHubAsync(CancellationToken ct)
        {
            var r = new Result();
            try
            {
                string api = "https://api.github.com/repos/" + ReleasesRepo + "/releases/latest";
                using var resp = await Http.GetAsync(api, ct);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    r.Error = "No release found (repo missing, private, or no release published yet).";
                    return r;
                }
                resp.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? "";
                r.LatestVersion = tag.TrimStart('v', 'V');
                r.Url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                r.Notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;

                if (!TryParseVersion(r.LatestVersion, out var latest))
                {
                    r.Error = "Could not read the version number from the release tag: " + tag;
                    return r;
                }

                r.UpdateAvailable = latest > Normalize(Current);
                r.Ok = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        static bool TryParseVersion(string s, out Version v)
        {
            v = new Version();
            if (string.IsNullOrWhiteSpace(s)) return false;
            // "1.2" বা "1.2.3-beta" এর মতো ফরম্যাট সামলানো
            int dash = s.IndexOfAny(new[] { '-', '+' });
            if (dash > 0) s = s.Substring(0, dash);
            if (!Version.TryParse(s, out var parsed)) return false;
            v = Normalize(parsed);
            return true;
        }

        // 1.2 -> 1.2.0.0, যাতে তুলনা ঠিক হয়
        static Version Normalize(Version v) =>
            new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

        // ---------- UI হেল্পার ----------
        // silent = true হলে (স্টার্টআপ চেক) শুধু নতুন ভার্সন থাকলেই কিছু দেখাবে
        public static async Task CheckAndShowAsync(IWin32Window owner, bool silent)
        {
            if (IsPackaged)
            {
                if (silent) return;      // Store ভার্সনে অটো চেক নেই, Store-ই আপডেট দেয়
                var ans = MessageBox.Show(owner,
                    "Updates for this version are delivered through the Microsoft Store.\n\nOpen the Store page now?",
                    "Check for updates", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ans == DialogResult.Yes) OpenStore();
                return;
            }

            Result r;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                r = await CheckGitHubAsync(cts.Token);
            }
            catch (Exception ex)
            {
                r = new Result { Error = ex.Message };
            }

            if (!r.Ok)
            {
                if (!silent)
                    MessageBox.Show(owner, "Could not check for updates.\n\n" + r.Error,
                        "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (r.UpdateAvailable)
            {
                string notes = string.IsNullOrWhiteSpace(r.Notes) ? "" :
                    "\n\nWhat's new:\n" + (r.Notes.Length > 600 ? r.Notes.Substring(0, 600) + "…" : r.Notes);
                var ans = MessageBox.Show(owner,
                    "A new version is available: v" + r.LatestVersion +
                    "\nYou have: v" + Current.ToString(3) + notes +
                    "\n\nOpen the download page?",
                    "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ans == DialogResult.Yes && !string.IsNullOrEmpty(r.Url)) OpenUrl(r.Url);
            }
            else if (!silent)
            {
                MessageBox.Show(owner, "You're up to date (v" + Current.ToString(3) + ").",
                    "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
