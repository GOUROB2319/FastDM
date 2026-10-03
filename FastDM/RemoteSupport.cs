#nullable disable
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using HtmlAgilityPack;
using Renci.SshNet;

namespace FastDM
{
    // ====================== সাধারণ টাইপ ======================
    public class AuthRequiredException : Exception
    {
        public AuthRequiredException() : base("Authentication required (username/password needed)") { }
    }

    public class RemoteNode
    {
        public string Name;
        public bool IsDir;
        public long Size;                       // 0 = অজানা
        public string Url;                      // শুধু ফাইলের জন্য
        public List<RemoteNode> Children = new List<RemoteNode>();
    }

    // ====================== ক্রেডেনশিয়াল স্টোর ======================
    // সেশন মেমোরি + (Remember দিলে) Windows Credential Manager
    public static class CredStore
    {
        static readonly Dictionary<string, KeyValuePair<string, string>> session =
            new Dictionary<string, KeyValuePair<string, string>>();

        public static string KeyFor(Uri u) =>
            (u.Scheme + "://" + u.Host + ":" + u.Port).ToLowerInvariant();

        public static bool TryGet(Uri u, out string user, out string pass)
        {
            user = null; pass = null;

            // ইউআরএলের ভেতরেই থাকলে (ftp://user:pass@host/)
            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                var parts = u.UserInfo.Split(new[] { ':' }, 2);
                user = Uri.UnescapeDataString(parts[0]);
                pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                return true;
            }

            string key = KeyFor(u);
            lock (session)
            {
                if (session.TryGetValue(key, out var kv)) { user = kv.Key; pass = kv.Value; return true; }
            }
            return ReadCred("FastDM:" + key, out user, out pass);
        }

        public static void Set(Uri u, string user, string pass, bool remember)
        {
            string key = KeyFor(u);
            lock (session) session[key] = new KeyValuePair<string, string>(user, pass);
            if (remember) WriteCred("FastDM:" + key, user, pass);
        }

        // ---- Win32 Credential Manager ----
        const uint CRED_TYPE_GENERIC = 1;
        const uint CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
        static extern bool CredWrite(ref CREDENTIAL cred, uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
        static extern bool CredRead(string target, uint type, uint flags, out IntPtr cred);

        [DllImport("advapi32.dll")]
        static extern void CredFree(IntPtr buffer);

        static void WriteCred(string target, string user, string pass)
        {
            byte[] blob = Encoding.Unicode.GetBytes(pass ?? "");
            IntPtr mem = Marshal.AllocHGlobal(Math.Max(1, blob.Length));
            try
            {
                if (blob.Length > 0) Marshal.Copy(blob, 0, mem, blob.Length);
                var c = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    UserName = user,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = mem,
                    Persist = CRED_PERSIST_LOCAL_MACHINE
                };
                CredWrite(ref c, 0);
            }
            catch { }
            finally { Marshal.FreeHGlobal(mem); }
        }

        static bool ReadCred(string target, out string user, out string pass)
        {
            user = null; pass = null;
            try
            {
                if (!CredRead(target, CRED_TYPE_GENERIC, 0, out IntPtr p)) return false;
                try
                {
                    var c = Marshal.PtrToStructure<CREDENTIAL>(p);
                    user = c.UserName;
                    pass = c.CredentialBlobSize > 0
                        ? Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2)
                        : "";
                    return true;
                }
                finally { CredFree(p); }
            }
            catch { return false; }
        }
    }

    // ====================== Engine এক্সটেনশন (partial) ======================
    public static partial class Engine
    {
        public static bool IsHttp(string url) =>
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // সেভ করা/সেশন ক্রেডেনশিয়াল থাকলে Basic Auth হেডার বসায়
        public static void ApplyAuth(HttpRequestMessage req)
        {
            if (req.RequestUri != null && CredStore.TryGet(req.RequestUri, out var u, out var p))
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(u + ":" + p)));
        }

        public static void CheckAuth(HttpResponseMessage resp)
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new AuthRequiredException();
        }

        public static async Task<string> GetPageAsync(Uri uri, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuth(req);
            using var resp = await Http.SendAsync(req, ct);
            CheckAuth(resp);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        // লিঙ্কটা ফোল্ডার কি না বোঝা
        public static async Task<(bool IsFolder, Uri Resolved)> DetectFolderAsync(Uri uri, CancellationToken ct)
        {
            string path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (path.EndsWith("/")) return (true, uri);              // শেষে / = ফোল্ডার
            if (!IsHttp(uri.AbsoluteUri)) return (false, uri);       // ftp/sftp: / ছাড়া = ফাইল

            string last = path.Substring(path.LastIndexOf('/') + 1);
            if (last.Contains('.')) return (false, uri);             // এক্সটেনশন আছে = ফাইল

            // এক্সটেনশনহীন http লিঙ্ক: সার্ভার / দিয়ে রিডাইরেক্ট করে HTML দিলে ফোল্ডার
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuth(req);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var final = resp.RequestMessage?.RequestUri ?? uri;
            bool html = resp.Content.Headers.ContentType?.MediaType?.Contains("html") == true;
            bool folder = resp.IsSuccessStatusCode && html && final.AbsolutePath.EndsWith("/");
            return (folder, folder ? final : uri);
        }

        // কিউ থেকে চালানোর মূল এন্ট্রি: http হলে আগের ইঞ্জিন, নইলে ftp/sftp
        public static async Task RunItemAsync(DownloadItem it, int connections, CancellationToken ct)
        {
            if (it.NeedsProbe)
            {
                await ProbeAsync(it, ct);
                it.NeedsProbe = false;
            }
            if (IsHttp(it.Url)) await RunAsync(it, connections, ct);
            else await RemoteTransfer.RunAsync(it, ct);
        }
    }

    // ====================== ফোল্ডার স্ক্যান (HTTP / FTP / SFTP) ======================
    public static class RemoteLister
    {
        public static async Task<RemoteNode> ScanAsync(Uri root, IProgress<int> progress, CancellationToken ct)
        {
            var node = new RemoteNode { IsDir = true, Name = "" };
            int count = 0;
            Action bump = () => progress?.Report(++count);

            try
            {
                switch (root.Scheme.ToLowerInvariant())
                {
                    case "http":
                    case "https":
                        await ScanHttp(root, node, 0, new HashSet<string>(), bump, ct);
                        break;
                    case "ftp":
                        await ScanFtp(root, node, bump, ct);
                        break;
                    case "sftp":
                        await Task.Run(() => ScanSftp(root, node, bump, ct), ct);
                        break;
                    default:
                        throw new NotSupportedException("Unsupported link type: " + root.Scheme);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && RemoteTransfer.IsAuthError(ex))
            {
                throw new AuthRequiredException();
            }
            return node;
        }

        // ---------- HTTP directory listing (Apache / nginx / h5ai ইত্যাদি) ----------
        static async Task ScanHttp(Uri dir, RemoteNode node, int depth, HashSet<string> seen,
                                   Action bump, CancellationToken ct)
        {
            if (depth > 30 || !seen.Add(dir.AbsoluteUri)) return;
            ct.ThrowIfCancellationRequested();

            string html = await Engine.GetPageAsync(dir, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
            if (anchors == null) return;

            string d = Uri.UnescapeDataString(dir.AbsolutePath);
            if (!d.EndsWith("/")) d += "/";
            var names = new HashSet<string>(StringComparer.Ordinal);
            var subs = new List<KeyValuePair<Uri, RemoteNode>>();

            foreach (var a in anchors)
            {
                string href = HtmlEntity.DeEntitize(a.GetAttributeValue("href", "")).Trim();
                if (href.Length == 0 || href[0] == '#' || href[0] == '?' ||
                    href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;

                if (!Uri.TryCreate(dir, href, out var u)) continue;
                if (!u.Host.Equals(dir.Host, StringComparison.OrdinalIgnoreCase) || u.Port != dir.Port) continue;

                string p = Uri.UnescapeDataString(u.AbsolutePath);
                if (p.Length <= d.Length || !p.StartsWith(d, StringComparison.Ordinal)) continue;   // parent/sort লিঙ্ক বাদ

                string rel = p.Substring(d.Length);
                bool isDir = rel.EndsWith("/");
                string seg = rel.TrimEnd('/');
                if (seg.Length == 0 || seg.Contains('/')) continue;       // শুধু সরাসরি সন্তান
                if (!names.Add(seg + (isDir ? "/" : ""))) continue;

                var clean = new Uri(u.GetLeftPart(UriPartial.Query));
                var child = new RemoteNode { Name = seg, IsDir = isDir };
                node.Children.Add(child);
                if (isDir) subs.Add(new KeyValuePair<Uri, RemoteNode>(clean, child));
                else { child.Url = clean.AbsoluteUri; bump(); }
            }

            foreach (var kv in subs)
                await ScanHttp(kv.Key, kv.Value, depth + 1, seen, bump, ct);
        }

        // ---------- FTP ----------
        static async Task ScanFtp(Uri root, RemoteNode node, Action bump, CancellationToken ct)
        {
            using var c = RemoteTransfer.CreateFtp(root);
            await c.Connect(ct);
            await WalkFtp(c, RemoteTransfer.PathOf(root), node, root, 0, bump, ct);
        }

        static async Task WalkFtp(AsyncFtpClient c, string path, RemoteNode node, Uri root,
                                  int depth, Action bump, CancellationToken ct)
        {
            if (depth > 30) return;
            var list = await c.GetListing(path, ct);
            foreach (var f in list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (f.Name == "." || f.Name == "..") continue;

                if (f.Type == FtpObjectType.Directory)
                {
                    var child = new RemoteNode { Name = f.Name, IsDir = true };
                    node.Children.Add(child);
                    await WalkFtp(c, f.FullName, child, root, depth + 1, bump, ct);
                }
                else if (f.Type == FtpObjectType.File)
                {
                    node.Children.Add(new RemoteNode
                    {
                        Name = f.Name,
                        Size = Math.Max(0L, f.Size),
                        Url = RemoteTransfer.BuildUrl(root, f.FullName)
                    });
                    bump();
                }
            }
        }

        // ---------- SFTP ----------
        static void ScanSftp(Uri root, RemoteNode node, Action bump, CancellationToken ct)
        {
            using var c = RemoteTransfer.CreateSftp(root);
            c.Connect();
            WalkSftp(c, RemoteTransfer.PathOf(root), node, root, 0, bump, ct);
        }

        static void WalkSftp(SftpClient c, string path, RemoteNode node, Uri root,
                             int depth, Action bump, CancellationToken ct)
        {
            if (depth > 30) return;
            foreach (var f in c.ListDirectory(path).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (f.Name == "." || f.Name == "..") continue;

                if (f.IsDirectory)
                {
                    var child = new RemoteNode { Name = f.Name, IsDir = true };
                    node.Children.Add(child);
                    WalkSftp(c, f.FullName, child, root, depth + 1, bump, ct);
                }
                else if (f.IsRegularFile)
                {
                    node.Children.Add(new RemoteNode
                    {
                        Name = f.Name,
                        Size = Math.Max(0L, f.Length),
                        Url = RemoteTransfer.BuildUrl(root, f.FullName)
                    });
                    bump();
                }
            }
        }
    }

    // ====================== FTP / SFTP ফাইল ট্রান্সফার ======================
    public static class RemoteTransfer
    {
        // নাম দিয়ে চেক, যাতে লাইব্রেরির নেমস্পেস ভার্সন অনুযায়ী না ভাঙে
        public static bool IsAuthError(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is AuthRequiredException) return true;
                string n = e.GetType().Name;
                if (n == "FtpAuthenticationException" || n == "SshAuthenticationException") return true;
            }
            return false;
        }

        public static string PathOf(Uri u)
        {
            string p = Uri.UnescapeDataString(u.AbsolutePath);
            return p.Length == 0 ? "/" : p;
        }

        public static string BuildUrl(Uri root, string fullPath)
        {
            string esc = string.Join("/", fullPath.Split('/').Select(Uri.EscapeDataString));
            if (!esc.StartsWith("/")) esc = "/" + esc;
            return root.Scheme + "://" + root.Authority + esc;
        }

        public static AsyncFtpClient CreateFtp(Uri u)
        {
            string user = "anonymous", pass = "anonymous@";      // আগে অ্যানোনিমাস চেষ্টা
            if (CredStore.TryGet(u, out var cu, out var cp)) { user = cu; pass = cp; }
            return new AsyncFtpClient(u.Host, user, pass, u.Port > 0 ? u.Port : 21);
        }

        public static SftpClient CreateSftp(Uri u)
        {
            // SFTP-তে লগইন সবসময় লাগে
            if (!CredStore.TryGet(u, out var user, out var pass)) throw new AuthRequiredException();
            return NetworkHelper.CreateSftp(u.Host, u.Port > 0 ? u.Port : 22, user, pass);        }

        // সিঙ্গেল ফাইল লিঙ্কের সাইজ/নাম জানা
        public static async Task ProbeAsync(DownloadItem it, CancellationToken ct)
        {
            var uri = new Uri(it.Url);
            string path = PathOf(uri);
            long size;
            try
            {
                if (uri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase))
                {
                    using var c = CreateFtp(uri);
                    await c.Connect(ct);
                    size = await c.GetFileSize(path, -1, ct);
                }
                else
                {
                    size = await Task.Run(() =>
                    {
                        using var c = CreateSftp(uri);
                        c.Connect();
                        return c.GetAttributes(path).Size;
                    }, ct);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && IsAuthError(ex))
            {
                throw new AuthRequiredException();
            }

            it.TotalBytes = Math.Max(0L, size);
            it.SupportsRange = true;
            if (string.IsNullOrWhiteSpace(it.FileName))
                it.FileName = Engine.Sanitize(Path.GetFileName(path));
        }

        public static async Task RunAsync(DownloadItem it, CancellationToken ct)
        {
            Directory.CreateDirectory(it.Folder);
            var uri = new Uri(it.Url);
            string path = PathOf(uri);

            // একটাই স্ট্রিম, সিকোয়েনশিয়াল রাইট: .part ফাইলের সাইজই resume পজিশন
            long resume = 0;
            if (File.Exists(it.TempPath)) resume = new FileInfo(it.TempPath).Length;
            if (it.TotalBytes > 0 && resume > it.TotalBytes) resume = 0;
            it.Downloaded = resume;

            try
            {
                using (var fs = new FileStream(it.TempPath, FileMode.OpenOrCreate, FileAccess.Write,
                                               FileShare.ReadWrite, 1 << 16, true))
                {
                    fs.SetLength(resume);
                    fs.Seek(resume, SeekOrigin.Begin);

                    if (uri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase))
                        await RunFtp(it, uri, path, fs, resume, ct);
                    else
                        await RunSftp(it, uri, path, fs, resume, ct);
                }
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex) when (IsAuthError(ex))
            {
                throw new AuthRequiredException();
            }

            if (it.TotalBytes > 0 && new FileInfo(it.TempPath).Length != it.TotalBytes)
                throw new IOException("Download incomplete.");

            if (File.Exists(it.SavePath)) File.Delete(it.SavePath);
            File.Move(it.TempPath, it.SavePath);
        }

        static async Task RunFtp(DownloadItem it, Uri uri, string path, FileStream fs, long resume, CancellationToken ct)
        {
            using var c = CreateFtp(uri);
            await c.Connect(ct);
            using var counting = new CountingStream(fs, n => it.AddDownloaded(n));
            bool ok = await c.DownloadStream(counting, path, resume, null, ct);
            if (!ok)
            {
                ct.ThrowIfCancellationRequested();
                throw new IOException("FTP transfer failed.");
            }
        }

        static async Task RunSftp(DownloadItem it, Uri uri, string path, FileStream fs, long resume, CancellationToken ct)
        {
            using var c = CreateSftp(uri);
            await Task.Run(() => c.Connect(), ct);
            using var rs = c.OpenRead(path);
            if (resume > 0) rs.Seek(resume, SeekOrigin.Begin);

            var buf = new byte[1 << 16];
            int n;
            while ((n = await rs.ReadAsync(buf.AsMemory(), ct)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                it.AddDownloaded(n);
                await SpeedLimiter.ThrottleAsync(n, ct);
            }
        }
    }

    // লিখে ফেলা বাইট গুনে প্রগ্রেস দেখানোর জন্য র‍্যাপার (ভেতরের স্ট্রিম বন্ধ করে না)
    sealed class CountingStream : Stream
    {
        readonly Stream inner;
        readonly Action<int> onWrite;
        public CountingStream(Stream inner, Action<int> onWrite) { this.inner = inner; this.onWrite = onWrite; }

        public override bool CanRead => false;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        { inner.Write(buffer, offset, count); onWrite(count); }

        public override void Write(ReadOnlySpan<byte> buffer)
        { inner.Write(buffer); onWrite(buffer.Length); }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await SpeedLimiter.ThrottleAsync(count, ct);
            await inner.WriteAsync(buffer, offset, count, ct);
            onWrite(count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await SpeedLimiter.ThrottleAsync(buffer.Length, ct);
            await inner.WriteAsync(buffer, ct);
            onWrite(buffer.Length);
        }
    }
}
