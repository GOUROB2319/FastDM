using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        public long Size;                       // 0 = unknown
        public string Url = string.Empty;
        public List<RemoteNode> Children = new List<RemoteNode>();
        public bool IsTruncated;
    }

    public static class RemoteLister
    {
        public const int MaxDepth = 30;
        public const int MaxNodes = 10_000;
        public const int MaxDirectoryEntries = 1_000;

        sealed class ScanLimitReachedException : Exception { }

        sealed class ScanBudget
        {
            readonly IProgress<int>? progress;
            int nodes;
            int files;
            public ScanBudget(IProgress<int>? progress) => this.progress = progress;
            public void Register(RemoteNode node)
            {
                if (++nodes > MaxNodes)
                    throw new ScanLimitReachedException();
                if (!node.IsDir) progress?.Report(++files);
            }
        }

        // Kept for callers that need a bounded immediate-child listing. FolderDownloadForm uses ScanAsync
        // so its single tree includes the selected folder's descendants.
        public static async Task<RemoteNode> ListDirectoryAsync(Uri directory, IProgress<int>? progress, CancellationToken ct)
        {
            var root = new RemoteNode { IsDir = true, Name = "", Url = directory.AbsoluteUri };
            try
            {
                switch (directory.Scheme.ToLowerInvariant())
                {
                    case "http":
                    case "https":
                        root.Children.AddRange(await ListHttpChildren(directory, ct));
                        break;
                    case "ftp":
                        using (var client = RemoteTransfer.CreateFtp(directory))
                        {
                            await client.Connect(ct);
                            root.Children.AddRange(await ListFtpChildren(client, RemoteTransfer.PathOf(directory), directory, ct));
                        }
                        break;
                    case "sftp":
                        await Task.Run(() =>
                        {
                            using var client = RemoteTransfer.CreateSftp(directory);
                            client.Connect();
                            root.Children.AddRange(ListSftpChildren(client, RemoteTransfer.PathOf(directory), directory, ct));
                        }, ct);
                        break;
                    default:
                        throw new NotSupportedException("Unsupported link type: " + directory.Scheme);
                }
                if (root.Children.Count > MaxDirectoryEntries)
                    throw new InvalidDataException("This directory exceeds FastDM's 1,000 item display limit.");
                int fileCount = 0;
                foreach (var item in root.Children)
                    if (!item.IsDir) progress?.Report(++fileCount);
                return root;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && RemoteTransfer.IsAuthError(ex))
            {
                throw new AuthRequiredException();
            }
        }

        // Full scan is reserved for the user's explicit Download / playlist action and for the
        // existing "download whole folders automatically" preference.
        public static async Task<RemoteNode> ScanAsync(Uri root, IProgress<int>? progress, CancellationToken ct)
        {
            var node = new RemoteNode { IsDir = true, Name = "", Url = root.AbsoluteUri };
            var budget = new ScanBudget(progress);
            try
            {
                switch (root.Scheme.ToLowerInvariant())
                {
                    case "http":
                    case "https":
                        await ScanHttp(root, node, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), budget, ct);
                        break;
                    case "ftp":
                        using (var client = RemoteTransfer.CreateFtp(root))
                        {
                            await client.Connect(ct);
                            await WalkFtp(client, RemoteTransfer.PathOf(root), node, root, 0, new HashSet<string>(StringComparer.Ordinal), budget, ct);
                        }
                        break;
                    case "sftp":
                        await Task.Run(() =>
                        {
                            using var client = RemoteTransfer.CreateSftp(root);
                            client.Connect();
                            WalkSftp(client, RemoteTransfer.PathOf(root), node, root, 0, new HashSet<string>(StringComparer.Ordinal), budget, ct);
                        }, ct);
                        break;
                    default:
                        throw new NotSupportedException("Unsupported link type: " + root.Scheme);
                }
            }
            catch (ScanLimitReachedException)
            {
                node.IsTruncated = true;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && RemoteTransfer.IsAuthError(ex))
            {
                throw new AuthRequiredException();
            }
            return node;
        }

        static async Task ScanHttp(Uri dir, RemoteNode node, int depth, HashSet<string> seen, ScanBudget budget, CancellationToken ct)
        {
            if (depth > MaxDepth || !seen.Add(dir.AbsoluteUri)) return;
            ct.ThrowIfCancellationRequested();
            foreach (var child in await ListHttpChildren(dir, ct))
            {
                budget.Register(child);
                node.Children.Add(child);
                if (child.IsDir && Uri.TryCreate(child.Url, UriKind.Absolute, out var childUri))
                    await ScanHttp(childUri, child, depth + 1, seen, budget, ct);
            }
        }

        static async Task<List<RemoteNode>> ListHttpChildren(Uri dir, CancellationToken ct)
        {
            string html = await DownloadEngine.GetPageAsync(dir, ct, maxBytes: 2 * 1024 * 1024);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
            if (anchors == null) throw new InvalidDataException("The HTTP page is not a supported directory index.");

            string decodedDir = Uri.UnescapeDataString(dir.AbsolutePath);
            if (!decodedDir.EndsWith("/", StringComparison.Ordinal)) decodedDir += "/";
            var children = new List<RemoteNode>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasIndexMarker = IsKnownIndexPage(doc, anchors);

            foreach (var anchor in anchors)
            {
                string href = HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", "")).Trim();
                if (href.Length == 0 || href[0] == '#' || href[0] == '?' ||
                    href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

                if (!Uri.TryCreate(dir, href, out var target) ||
                    !target.Scheme.Equals(dir.Scheme, StringComparison.OrdinalIgnoreCase) ||
                    !target.IdnHost.Equals(dir.IdnHost, StringComparison.OrdinalIgnoreCase) || target.Port != dir.Port)
                    continue;

                string path = Uri.UnescapeDataString(target.AbsolutePath);
                if (!path.StartsWith(decodedDir, StringComparison.Ordinal)) continue;
                string relative = path.Substring(decodedDir.Length);
                bool isDir = relative.EndsWith("/", StringComparison.Ordinal);
                string name = relative.TrimEnd('/');
                if (name.Length == 0 || name.Contains('/')) continue;
                // Encoded separators and traversal must not become local or remote path components.
                if (name is "." or ".." || name.IndexOfAny(new[] { '\\', '\0' }) >= 0) continue;
                if (!names.Add(name)) continue;

                var cleanTarget = new UriBuilder(target) { Fragment = "" }.Uri;
                children.Add(new RemoteNode { Name = name, IsDir = isDir, Url = cleanTarget.AbsoluteUri });
                if (children.Count > MaxDirectoryEntries)
                    throw new InvalidDataException("This directory exceeds FastDM's 1,000 item listing limit.");
            }

            if (!hasIndexMarker)
                throw new InvalidDataException("The HTTP response is not a recognized directory index; no files were queued.");
            if (children.Count > MaxDirectoryEntries)
                throw new InvalidDataException("This directory exceeds FastDM's 1,000 item listing limit.");
            return children.OrderByDescending(x => x.IsDir).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static bool IsKnownIndexPage(HtmlDocument doc, HtmlNodeCollection anchors)
        {
            string title = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "").Trim();
            string heading = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText ?? "").Trim();
            if (title.StartsWith("Index of", StringComparison.OrdinalIgnoreCase) ||
                heading.StartsWith("Index of", StringComparison.OrdinalIgnoreCase)) return true;
            if (doc.DocumentNode.SelectSingleNode("//table[@id='list' or @id='indexlist' or @class='index']") != null ||
                anchors.Any(a => HtmlEntity.DeEntitize(a.InnerText).Trim().Equals("Parent Directory", StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        static async Task WalkFtp(AsyncFtpClient client, string path, RemoteNode node, Uri root,
            int depth, HashSet<string> seen, ScanBudget budget, CancellationToken ct)
        {
            if (depth > MaxDepth || !seen.Add(path)) return;
            foreach (var child in await ListFtpChildren(client, path, root, ct))
            {
                ct.ThrowIfCancellationRequested();
                budget.Register(child);
                node.Children.Add(child);
                if (child.IsDir) await WalkFtp(client, RemoteTransfer.PathOf(new Uri(child.Url)), child, root, depth + 1, seen, budget, ct);
            }
        }

        static async Task<List<RemoteNode>> ListFtpChildren(AsyncFtpClient client, string path, Uri root, CancellationToken ct)
        {
            var listing = await client.GetListing(path, ct);
            if (listing.Length > MaxDirectoryEntries)
                throw new InvalidDataException("This directory exceeds FastDM's 1,000 item listing limit.");
            var children = new List<RemoteNode>();
            foreach (var item in listing.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (item.Name is "." or "..") continue;
                if (item.Type == FtpObjectType.Directory)
                {
                    string url = RemoteTransfer.BuildUrl(root, item.FullName).TrimEnd('/') + "/";
                    children.Add(new RemoteNode { Name = item.Name, IsDir = true, Url = url });
                }
                else if (item.Type == FtpObjectType.File)
                {
                    children.Add(new RemoteNode { Name = item.Name, Size = Math.Max(0L, item.Size), Url = RemoteTransfer.BuildUrl(root, item.FullName) });
                }
            }
            return children.OrderByDescending(x => x.IsDir).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void WalkSftp(SftpClient client, string path, RemoteNode node, Uri root,
            int depth, HashSet<string> seen, ScanBudget budget, CancellationToken ct)
        {
            if (depth > MaxDepth || !seen.Add(path)) return;
            foreach (var child in ListSftpChildren(client, path, root, ct))
            {
                ct.ThrowIfCancellationRequested();
                budget.Register(child);
                node.Children.Add(child);
                if (child.IsDir) WalkSftp(client, RemoteTransfer.PathOf(new Uri(child.Url)), child, root, depth + 1, seen, budget, ct);
            }
        }

        static List<RemoteNode> ListSftpChildren(SftpClient client, string path, Uri root, CancellationToken ct)
        {
            var listing = client.ListDirectory(path).Take(MaxDirectoryEntries + 1).ToList();
            if (listing.Count > MaxDirectoryEntries)
                throw new InvalidDataException("This directory exceeds FastDM's 1,000 item listing limit.");
            var children = new List<RemoteNode>();
            foreach (var item in listing.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (item.Name is "." or ".." || item.IsSymbolicLink) continue;
                if (item.IsDirectory)
                {
                    string url = RemoteTransfer.BuildUrl(root, item.FullName).TrimEnd('/') + "/";
                    children.Add(new RemoteNode { Name = item.Name, IsDir = true, Url = url });
                }
                else if (item.IsRegularFile)
                {
                    children.Add(new RemoteNode { Name = item.Name, Size = Math.Max(0L, item.Length), Url = RemoteTransfer.BuildUrl(root, item.FullName) });
                }
            }
            return children.OrderByDescending(x => x.IsDir).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
