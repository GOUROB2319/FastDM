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
                                                       IReadOnlyCollection<BridgeCookie>? cookies = null)
        {
            using var client = CreateCookieClient(uri.AbsoluteUri, cookies);
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAuth(req);
            using var resp = await client.SendAsync(req, ct);
            CheckAuth(resp);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        // লিঙ্কটা ফোল্ডার কি না বোঝা
        public static async Task<(bool IsFolder, Uri Resolved)> DetectFolderAsync(Uri uri, CancellationToken ct,
                                                                                   IReadOnlyCollection<BridgeCookie>? cookies = null)
        {
            string path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (path.EndsWith("/")) return (true, uri);              // শেষে / = ফোল্ডার
            if (!IsHttp(uri.AbsoluteUri)) return (false, uri);       // ftp/sftp: / ছাড়া = ফাইল

            string last = path.Substring(path.LastIndexOf('/') + 1);
            if (last.Contains('.')) return (false, uri);             // এক্সটেনশন আছে = ফাইল

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
