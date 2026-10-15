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
    // ====================== Engine এক্সটেনশন (partial) ======================
    public static partial class DownloadEngine
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

        public static async Task<string> GetPageAsync(Uri uri, CancellationToken ct,
                                                       IReadOnlyCollection<BridgeCookie>? cookies = null,
                                                       int maxBytes = 2 * 1024 * 1024)
        {
            using var client = CreateCookieClient(uri.AbsoluteUri, cookies);
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuth(req);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            CheckAuth(resp);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength is long length && length > maxBytes)
                throw new InvalidDataException("The remote directory page exceeds FastDM's 2 MB safety limit.");
            await using var source = await resp.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
            var chunk = new byte[16 * 1024];
            while (true)
            {
                int read = await source.ReadAsync(chunk.AsMemory(), ct);
                if (read == 0) break;
                if (buffer.Length + read > maxBytes)
                    throw new InvalidDataException("The remote directory page exceeds FastDM's 2 MB safety limit.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            }
            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return await reader.ReadToEndAsync(ct);
        }

        // লিঙ্কটা ফোল্ডার কি না বোঝা
        public static async Task<(bool IsFolder, Uri Resolved)> DetectFolderAsync(Uri uri, CancellationToken ct,
                                                                                   IReadOnlyCollection<BridgeCookie>? cookies = null)
        {
            string path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (path.EndsWith("/")) return (true, uri);              // শেষে / = ফোল্ডার
            string last = path.Substring(path.LastIndexOf('/') + 1);
            string scheme = uri.Scheme.ToLowerInvariant();
            if (scheme == "ftp")
            {
                try
                {
                    using var ftpClient = RemoteTransfer.CreateFtp(uri);
                    await ftpClient.Connect(ct);
                    string target = RemoteTransfer.PathOf(uri).TrimEnd('/');
                    int slash = target.LastIndexOf('/');
                    string parent = slash <= 0 ? "/" : target.Substring(0, slash);
                    string name = target.Substring(slash + 1);
                    var listing = await ftpClient.GetListing(parent, ct);
                    bool isFolder = listing.Any(item => item.Type == FtpObjectType.Directory &&
                        (item.FullName.TrimEnd('/').Equals(target, StringComparison.OrdinalIgnoreCase) ||
                         item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
                    return (isFolder, uri);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && RemoteTransfer.IsAuthError(ex))
                {
                    throw new AuthRequiredException();
                }
            }
            if (scheme == "sftp")
            {
                try
                {
                    bool isFolder = await Task.Run(() =>
                    {
                        using var sftpClient = RemoteTransfer.CreateSftp(uri);
                        sftpClient.Connect();
                        return sftpClient.GetAttributes(RemoteTransfer.PathOf(uri)).IsDirectory;
                    }, ct);
                    return (isFolder, uri);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && RemoteTransfer.IsAuthError(ex))
                {
                    throw new AuthRequiredException();
                }
            }
            if (last.Contains('.')) return (false, uri);             // HTTP extension usually identifies a file.
            if (!IsHttp(uri.AbsoluteUri)) return (false, uri);

            // এক্সটেনশনহীন http লিঙ্ক: সার্ভার / দিয়ে রিডাইরেক্ট করে HTML দিলে ফোল্ডার
            using var client = CreateCookieClient(uri.AbsoluteUri, cookies);
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuth(req);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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
}
