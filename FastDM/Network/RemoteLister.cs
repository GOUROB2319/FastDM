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
    public class RemoteNode
    {
        public string Name = string.Empty;
        public bool IsDir;
        public long Size;                       // 0 = অজানা
        public string Url = string.Empty;                      // শুধু ফাইলের জন্য
        public List<RemoteNode> Children = new List<RemoteNode>();
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

            string html = await DownloadEngine.GetPageAsync(dir, ct);
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
}
