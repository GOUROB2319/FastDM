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
using System.Diagnostics;

namespace FastDM
{
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
                it.FileName = DownloadEngine.Sanitize(Path.GetFileName(path));
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

    // SFTP server identity check, "trust on first use" like OpenSSH: the first time a server is seen its
    // key fingerprint is saved; later connections must present the same key, otherwise the connection is
    // refused (a changed key can mean someone is intercepting the connection).
    // The server was legitimately reinstalled? Delete its line (or the whole file) at
    // %LocalAppData%\FastDM\known_hosts.json and connect again.
    public static class SshHostKeys
    {
        static readonly object gate = new object();
        static Dictionary<string, string>? known;

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "FastDM", "known_hosts.json");

        public static bool Verify(string host, int port, string? keyType, byte[]? keyBytes)
        {
            if (keyBytes == null || keyBytes.Length == 0) return false;

            string id = host.Trim().ToLowerInvariant() + ":" + port;
            string print = (keyType ?? "") + " SHA256:" +
                           Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(keyBytes));

            lock (gate)
            {
                known ??= Load();

                if (known.TryGetValue(id, out var saved))
                {
                    if (string.Equals(saved, print, StringComparison.Ordinal)) return true;

                    AppLog.Write("SFTP: REFUSED " + id + " - the server key changed (saved " + saved + ", now " + print + "). " +
                                 "If the server was reinstalled, remove its line from " + FilePath + ".");
                    return false;
                }

                known[id] = print;
                Save();
                AppLog.Write("SFTP: remembered the key of " + id + " (" + print + ")");
                return true;
            }
        }

        static Dictionary<string, string> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))
                           ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                AppLog.Write("SFTP: could not read " + FilePath + ": " + ex.Message);
            }
            return new Dictionary<string, string>();
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(known,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex)
            {
                AppLog.Write("SFTP: could not save " + FilePath + ": " + ex.Message);
            }
        }
    }
}
