using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastDM
{
    // MainForm part: adding links: dialogs, batch, folders, media, yt-dlp.
    public partial class MainForm
    {
        async void ShowAddDialog(
            string url,
            RequestContext? ctx = null)
        {
            try
            {
                await addGate.WaitAsync();

                try
                {
                    await ShowAddDialogCore(url, ctx);
                }
                finally
                {
                    addGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                AppLog.Write("MainForm.ShowAddDialog: cancelled");
            }
            catch (Exception exGuard)
            {
                Program.ReportError("MainForm.ShowAddDialog", exGuard);
            }
        }

        // এক্সটেনশনের "Download with FastDM": ডায়ালগ ছাড়া, ডিফল্ট ফোল্ডারে সরাসরি শুরু।
        // (ফোল্ডার লিঙ্ক, ভিডিও পেজ বা yt-dlp সাইটে ইউজারের পছন্দ লাগে, তাই সেসব ক্ষেত্রে নিজস্ব উইন্ডো আসবেই)
        async void QuickAdd(
            string url,
            RequestContext ctx)
        {
            try
            {
                await addGate.WaitAsync();

                try
                {
                    await ProcessUrlsAsync(
                        new[] { url },
                        "",
                        settings.DefaultFolder,
                        true,
                        ctx);
                }
                catch (Exception ex)
                {
                    lblActive.Text =
                        "Could not add link: " + ex.Message;
                }
                finally
                {
                    addGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                AppLog.Write("MainForm.QuickAdd: cancelled");
            }
            catch (Exception exGuard)
            {
                Program.ReportError("MainForm.QuickAdd", exGuard);
            }
        }

        async Task ShowAddDialogCore(
            string url,
            RequestContext ctx)
        {
            using var dlg =
                new AddUrlForm(
                    url,
                    settings.DefaultFolder);

            Theme.Apply(dlg);

            var result =
                dlg.ShowDialog(this);

            if (result != DialogResult.OK &&
                result != DialogResult.Yes)
                return;

            bool startNow =
                result == DialogResult.OK;

            string[] urls = dlg.Urls;
            string nameText =
                dlg.FileNameText;

            string folder =
                dlg.Folder;

            // A list from a file, or several links that include video-site links: check them in the import
            // window first (one picker per video would take forever and could expand playlists).
            if (dlg.FromFile ||
                (urls.Length >= 2 && urls.Any(YtDlpSites.IsSupported)))
            {
                await StartBatchImportAsync(
                    dlg.RawText,
                    folder);

                return;
            }

            await ProcessUrlsAsync(
                urls,
                nameText,
                folder,
                startNow,
                ctx);
        }

        // Add ডায়ালগ আর এক্সটেনশনের সরাসরি ডাউনলোড, দুই পথেরই মূল কাজ
        async Task ProcessUrlsAsync(
            string[] urls,
            string nameText,
            string folder,
            bool startNow,
            RequestContext ctx)
        {
            // একবারে সর্বোচ্চ কয়টা লিঙ্ক (Preferences → Downloads)
            int maxBatch = Math.Max(1, settings.MaxBatchUrls);

            if (urls.Length > maxBatch)
            {
                MessageBox.Show(
                    this,
                    "Only the first " + maxBatch +
                    " of " + urls.Length +
                    " links will be added.\n(You can change this limit in Preferences → Downloads.)",
                    "FastDM");

                urls = urls.Take(maxBatch).ToArray();
            }

            lblActive.Text =
                "Checking link(s)…";

            var fileUrls =
                new List<string>();

            var folderUris =
                new List<Uri>();

            var mediaItems =
                new List<MediaProbeResult>();

            var ytItems =
                new List<string>();

            foreach (var u in urls)
            {
                // Sites handled by yt-dlp go first. A Facebook or Instagram reel link ends with "/" and used to be
                // taken for a folder, and the folder check also wasted a web request on every YouTube link.
                if (YtDlpSites.IsSupported(u))
                {
                    ytItems.Add(u);
                    continue;
                }

                try
                {
                    using var cts =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(20));

                    var (isFolder, resolved) =
                        await DownloadEngine.DetectFolderAsync(
                            new Uri(u),
                            cts.Token);

                    if (isFolder)
                    {
                        folderUris.Add(resolved);
                        continue;
                    }
                }
                catch (Exception exIgnored1) { AppLog.Write("MainForm.Add (ignored): " + exIgnored1.Message); }

                if (DownloadEngine.IsHttp(u))
                {
                    try
                    {
                        using var mcts =
                            new CancellationTokenSource(
                                TimeSpan.FromSeconds(20));

                        var media =
                            await MediaDetector.ProbeAsync(
                                new Uri(u),
                                mcts.Token);

                        if (media.Kind ==
                                MediaKind.Hls ||
                            media.Kind ==
                                MediaKind.Dash ||
                            media.Kind ==
                                MediaKind.Page)
                        {
                            mediaItems.Add(media);
                            continue;
                        }
                    }
                    catch (Exception exIgnored2) { AppLog.Write("MainForm.Add (ignored): " + exIgnored2.Message); }
                }

                fileUrls.Add(u);
            }

            // ফাইল লিঙ্ক
            lblActive.Text =
                "Fetching file info…";

            int addedCount = 0;
            int skippedPages = 0;

            foreach (var u in fileUrls)
            {
                var it = new DownloadItem
                {
                    Url = u,
                    Folder = folder,
                    Referer = ctx?.Referer,
                    UserAgent = ctx?.UserAgent,
                    FileName =
                        fileUrls.Count == 1 &&
                        folderUris.Count == 0 &&
                        nameText.Length > 0
                            ? DownloadEngine.Sanitize(
                                nameText)
                            : ""
                };

                bool retry = true;

                while (retry)
                {
                    retry = false;

                    try
                    {
                        using var cts =
                            new CancellationTokenSource(
                                TimeSpan.FromSeconds(25));

                        await DownloadEngine.ProbeAsync(
                            it,
                            cts.Token);
                    }
                    catch (AuthRequiredException)
                    {
                        if (CredentialForm.Prompt(
                                this,
                                new Uri(u)))
                        {
                            retry = true;
                        }
                    }
                    catch (Exception exIgnored3) { AppLog.Write("MainForm.Add (ignored): " + exIgnored3.Message); }
                }

                if (string.IsNullOrWhiteSpace(
                        it.FileName))
                {
                    it.FileName =
                        DownloadEngine.NameFromUrl(u);
                }

                // "Do not download web pages": উত্তর HTML পেজ হলে বাদ (.html লিঙ্ক ছাড়া)
                if (settings.SkipWebPages &&
                    string.Equals(
                        it.ContentType,
                        "text/html",
                        StringComparison.OrdinalIgnoreCase) &&
                    !IsHtmlName(
                        u))
                {
                    skippedPages++;
                    continue;
                }

                it.Folder =
                    SmartFolder(
                        folder,
                        it.FileName,
                        u);

                it.FileName =
                    UniqueName(
                        it.Folder,
                        it.FileName);

                it.State =
                    startNow
                        ? DlState.Queued
                        : DlState.Paused;

                items.Add(it);
                selectNewId = it.Id;
                addedCount++;
            }

            if (skippedPages > 0)
            {
                lblActive.Text =
                    "Skipped " + skippedPages +
                    " web page link(s). (Preferences → Downloads → Do not download web pages)";
            }

            if (addedCount > 0 &&
                settings.NotifyAdded)
            {
                Notify(
                    "Download added",
                    addedCount == 1
                        ? items[items.Count - 1].FileName
                        : addedCount + " downloads added");
            }

            MarkChanged();
            SaveState();

            // ফোল্ডার
            foreach (var fu in folderUris)
                AddFolder(
                    fu,
                    startNow);

            // মিডিয়া
            foreach (var mi in mediaItems)
                await AddMediaAsync(
                    mi,
                    folder,
                    startNow);

            // yt-dlp
            foreach (var yu in ytItems)
                await AddYtDlpAsync(
                    yu,
                    folder,
                    startNow);
        }

        // Drag-and-drop of a list file
        async void ImportListFile(string path)
        {
            try
            {
                await addGate.WaitAsync();

                try
                {
                    if (new FileInfo(path).Length > 5 * 1024 * 1024)
                    {
                        MessageBox.Show(
                            this,
                            "This file is too big for a list of links (limit 5 MB).",
                            "FastDM");

                        return;
                    }

                    await StartBatchImportAsync(
                        File.ReadAllText(path),
                        settings.DefaultFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "FastDM");
                }
                finally
                {
                    addGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                AppLog.Write("MainForm.ImportListFile: cancelled");
            }
            catch (Exception exGuard)
            {
                Program.ReportError("MainForm.ImportListFile", exGuard);
            }
        }

        // Check the list line by line, show the preview, then add what the user ticked.
        async Task StartBatchImportAsync(
            string text,
            string folder)
        {
            var listed =
                items
                    .Select(i => i.YtDlp?.Url ?? i.Url ?? "")
                    .ToList();

            var rows =
                BatchImport.Parse(
                    text,
                    listed);

            if (rows.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No links were found in this text.",
                    "FastDM");

                return;
            }

            using var dlg =
                new BatchImportForm(
                    rows,
                    folder,
                    settings.MaxBatchUrls);

            Theme.Apply(dlg);

            var result =
                dlg.ShowDialog(this);

            if (result != DialogResult.OK &&
                result != DialogResult.Yes)
                return;

            await AddBatchAsync(
                dlg.SelectedRows,
                dlg.Quality,
                dlg.FoldersAutomatically,
                dlg.SaveFolder,
                result == DialogResult.OK);
        }

        async Task AddBatchAsync(
            List<BatchRow> chosenRows,
            BatchQuality quality,
            bool foldersAutomatically,
            string folder,
            bool startNow)
        {
            // never more than the limit from Preferences > Downloads
            int cap = Math.Max(1, settings.MaxBatchUrls);
            var chosen = chosenRows.Take(cap).ToList();
            int overCap = chosenRows.Count - chosen.Count;

            int videos = 0;
            var folderUris = new List<Uri>();
            var otherUrls = new List<string>();

            foreach (var r in chosen)
            {
                if (r.Kind == BatchKind.Video)
                {
                    AddBatchVideo(r.Text, quality, folder, startNow);
                    videos++;
                }
                else if (r.Kind == BatchKind.Folder)
                {
                    if (Uri.TryCreate(r.Text, UriKind.Absolute, out var fu))
                        folderUris.Add(fu);
                }
                else if (r.Kind == BatchKind.File)
                {
                    otherUrls.Add(r.Text);
                }
            }

            MarkChanged();
            SaveState();

            // files and other links: the normal path (size, name, duplicates)
            int before = items.Count;

            if (otherUrls.Count > 0)
            {
                await ProcessUrlsAsync(
                    otherUrls.ToArray(),
                    "",
                    folder,
                    startNow,
                    new RequestContext());
            }

            int others = items.Count - before;

            // folders
            int folderFiles = 0;
            int folderNo = 0;

            foreach (var fu in folderUris)
            {
                folderNo++;

                if (foldersAutomatically)
                {
                    lblActive.Text =
                        "Folder " + folderNo + " of " + folderUris.Count + "...";

                    folderFiles +=
                        await AddFolderAllAsync(
                            fu,
                            folder,
                            startNow);
                }
                else
                {
                    AddFolder(
                        fu,
                        startNow);
                }
            }

            MarkChanged();
            SaveState();

            var parts = new List<string>();
            if (videos > 0) parts.Add(videos + (videos == 1 ? " video" : " videos"));
            if (folderFiles > 0) parts.Add(folderFiles + (folderFiles == 1 ? " file from folders" : " files from folders"));
            if (others > 0) parts.Add(others + (others == 1 ? " other file" : " other files"));

            string text =
                parts.Count > 0
                    ? "Added: " + string.Join(", ", parts) + "."
                    : "Nothing was added.";

            if (overCap > 0)
                text += " " + overCap + " more were left out (limit " + cap + ").";

            lblActive.Text = text;
        }

        // A video in a batch: no lookup first. The file is named after the title when it is downloaded.
        void AddBatchVideo(
            string url,
            BatchQuality quality,
            string folder,
            bool startNow)
        {
            string label = "video";

            if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                string host =
                    u.Host.StartsWith("www.")
                        ? u.Host.Substring(4)
                        : u.Host;

                string tail =
                    BatchImport.YouTubeId(u) ??
                    u.AbsolutePath.Trim('/').Split('/').LastOrDefault() ??
                    "";

                label = host + (tail.Length > 0 ? " " + tail : "");
            }

            string ext =
                quality.AudioOnly
                    ? "." + quality.AudioFormat
                    : ".mp4";

            var it = new DownloadItem
            {
                Url = url,
                Folder = folder,
                FileName = DownloadEngine.Sanitize(label) + ext,
                YtDlp = new YtDlpSpec
                {
                    Url = url,
                    Format = quality.Format,
                    AudioOnly = quality.AudioOnly,
                    AudioFormat = quality.AudioFormat,
                    NameFromTitle = true
                },
                State =
                    startNow
                        ? DlState.Queued
                        : DlState.Paused
            };

            items.Add(it);
            selectNewId = it.Id;
        }

        // A whole folder, without the tree window: every file goes into <folder>\<name of the remote folder>.
        // Returns how many files were added.
        async Task<int> AddFolderAllAsync(
            Uri uri,
            string baseFolder,
            bool startNow)
        {
            RemoteNode? root = null;

            while (root == null)
            {
                try
                {
                    lblActive.Text = "Scanning " + uri.Host + "...";

                    using var cts =
                        new CancellationTokenSource(
                            TimeSpan.FromMinutes(10));

                    root =
                        await RemoteLister.ScanAsync(
                            uri,
                            new Progress<int>(n =>
                                lblActive.Text =
                                    "Scanning " + uri.Host + "... " + n + " file(s) found"),
                            cts.Token);
                }
                catch (AuthRequiredException)
                {
                    if (!CredentialForm.Prompt(this, uri))
                        return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Could not scan this folder:\n" + uri + "\n\n" +
                        ex.GetBaseException().Message,
                        "FastDM",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return 0;
                }
            }

            string leaf =
                Uri.UnescapeDataString(uri.AbsolutePath)
                   .TrimEnd('/');

            leaf = leaf.Substring(leaf.LastIndexOf('/') + 1);

            if (leaf.Length == 0)
                leaf = uri.Host;

            string target =
                Path.Combine(
                    baseFolder,
                    SafeFolderName(leaf));

            int count = 0;
            AddFolderNode(root, target, startNow, ref count);

            return count;
        }

        static string SafeFolderName(string name)
        {
            string s = DownloadEngine.Sanitize(name).TrimEnd('.', ' ');
            return s.Length == 0 ? "_" : s;
        }

        void AddFolderNode(
            RemoteNode node,
            string dir,
            bool startNow,
            ref int count)
        {
            foreach (var c in node.Children)
            {
                if (c.IsDir)
                {
                    AddFolderNode(
                        c,
                        Path.Combine(dir, SafeFolderName(c.Name)),
                        startNow,
                        ref count);

                    continue;
                }

                if (string.IsNullOrEmpty(c.Url))
                    continue;

                var it = new DownloadItem
                {
                    Url = c.Url,
                    Folder = dir,
                    FileName =
                        UniqueName(
                            dir,
                            DownloadEngine.Sanitize(c.Name)),
                    TotalBytes = c.Size,
                    NeedsProbe = DownloadEngine.IsHttp(c.Url),
                    State =
                        startNow
                            ? DlState.Queued
                            : DlState.Paused
                };

                items.Add(it);
                selectNewId = it.Id;
                count++;
            }
        }

        async Task AddMediaAsync(
            MediaProbeResult r,
            string saveFolder,
            bool startNow)
        {
            string url = r.Url;
            string? referer = null;
            MediaKind kind = r.Kind;

            if (kind == MediaKind.Page)
            {
                MediaCandidate pick;

                if (r.Candidates.Count == 1)
                {
                    pick = r.Candidates[0];
                }
                else
                {
                    using var cf =
                        new CandidateForm(
                            r.Candidates);

                    Theme.Apply(cf);

                    if (cf.ShowDialog(this) !=
                        DialogResult.OK)
                    {
                        return;
                    }

                    if (cf.Picked is not MediaCandidate candidate) return;
                    pick = candidate;
                }

                referer = r.Url;

                if (pick.Kind ==
                    MediaKind.Direct)
                {
                    await AddDirectCandidateAsync(
                        pick.Url,
                        saveFolder,
                        startNow);

                    return;
                }

                url = pick.Url;
                kind = pick.Kind;
            }

            MediaOptions? opts = null;

            lblActive.Text =
                "Reading stream info…";

            while (opts == null)
            {
                try
                {
                    using var cts =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(25));

                    opts =
                        await MediaParser.LoadAsync(
                            url,
                            kind,
                            referer,
                            r.Title,
                            cts.Token);
                }
                catch (AuthRequiredException)
                {
                    if (!CredentialForm.Prompt(
                            this,
                            new Uri(url)))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Could not read this stream.\n\n" +
                        ex.GetBaseException().Message,
                        "Video",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }
            }

            using var pf =
                new MediaPickerForm(
                    opts,
                    saveFolder);

            Theme.Apply(pf);

            if (pf.ShowDialog(this) !=
                DialogResult.OK)
                return;

            string folder = pf.SaveFolder;

            var spec = pf.Spec;
            if (spec == null) return;
            string ext =
                spec.AudioOnly
                    ? ".m4a"
                    : ".mp4";

            var it = new DownloadItem
            {
                Url = url,
                Folder = folder,
                FileName =
                    UniqueName(
                        folder,
                        DownloadEngine.Sanitize(
                            pf.FileName) +
                        ext),
                Stream = spec,
                State =
                    startNow
                        ? DlState.Queued
                        : DlState.Paused
            };

            items.Add(it);
            selectNewId = it.Id;
            MarkChanged();
            SaveState();
        }

        async Task AddYtDlpAsync(
            string url,
            string saveFolder,
            bool startNow)
        {
            if (YtDlpLocator.Find() == null)
            {
                MessageBox.Show(
                    this,
                    "yt-dlp.exe was not found.\nPut yt-dlp.exe (and deno.exe) in the Tools folder next to the app.",
                    "Video",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            string path =
                new Uri(url)
                    .AbsolutePath
                    .ToLowerInvariant();

            if (path.StartsWith(
                    "/playlist"))
            {
                MessageBox.Show(
                    this,
                    "Playlist links are not supported yet.\nOpen a single video link instead.",
                    "Video",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            lblActive.Text =
                "Reading video info…";

            UseWaitCursor = true;

            var readWatch = Stopwatch.StartNew();

            using var readTick =
                new System.Windows.Forms.Timer { Interval = 1000 };

            readTick.Tick += (s, e) =>
                lblActive.Text =
                    "Reading video info\u2026 " +
                    (int)readWatch.Elapsed.TotalSeconds +
                    " s";

            readTick.Start();

            YtInfo info;

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(90));

                info =
                    await YtDlpInfo.LoadAsync(
                        url,
                        cts.Token);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show(
                    this,
                    "Reading the video info took too long.",
                    "Video",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }
            catch (Exception ex)
            {
                string hint =
                    YtDlpCookies.HintFor(ex.GetBaseException().Message) ??
                    (UpdateChecker.IsPackaged
                        ? "\n\nIf this keeps happening, a newer app version (with an updated yt-dlp) may fix it."
                        : "\n\nTry Settings → Update yt-dlp.");

                MessageBox.Show(
                    this,
                    "Could not read this video.\n\n" +
                    ex.GetBaseException().Message +
                    hint,
                    "Video",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }
            finally
            {
                readTick.Stop();
                UseWaitCursor = false;
            }

            using var pf =
                new YtPickerForm(
                    info,
                    url,
                    saveFolder);

            Theme.Apply(pf);

            if (pf.ShowDialog(this) !=
                DialogResult.OK)
                return;

            string folder =
                pf.SaveFolder;

            var spec = pf.Spec;
            if (spec == null) return;
            string ext =
                spec.AudioOnly
                    ? "." + pf.Spec.AudioFormat
                    : ".mp4";

            var it = new DownloadItem
            {
                Url = url,
                Folder = folder,
                FileName =
                    UniqueName(
                        folder,
                        pf.Spec.BaseName +
                        ext),
                YtDlp = pf.Spec,
                State =
                    startNow
                        ? DlState.Queued
                        : DlState.Paused
            };

            items.Add(it);
            selectNewId = it.Id;

            MarkChanged();
            SaveState();
        }

        async Task AddDirectCandidateAsync(
            string url,
            string saveFolder,
            bool startNow)
        {
            var it = new DownloadItem
            {
                Url = url,
                Folder = saveFolder,
                FileName = ""
            };

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(25));

                await DownloadEngine.ProbeAsync(
                    it,
                    cts.Token);
            }
            catch (Exception exIgnored4) { AppLog.Write("MainForm.Add (ignored): " + exIgnored4.Message); }

            if (string.IsNullOrWhiteSpace(
                    it.FileName))
            {
                it.FileName =
                    DownloadEngine.NameFromUrl(url);
            }

            it.FileName =
                UniqueName(
                    saveFolder,
                    it.FileName);

            it.State =
                startNow
                    ? DlState.Queued
                    : DlState.Paused;

            items.Add(it);
            selectNewId = it.Id;

            MarkChanged();
            SaveState();
        }

        void AddFolder(
            Uri uri,
            bool startNow,
            bool playlistMode = false)
        {
            using var f =
                new FolderDownloadForm(
                    uri,
                    settings.DefaultFolder,
                    settings.MaxSimultaneous,
                    playlistMode);

            Theme.Apply(f);

            if (f.ShowDialog(this) !=
                DialogResult.OK)
                return;

            settings.MaxSimultaneous =
                f.Simultaneous;

            var localPaths =
                new List<string>();

            foreach (var pf in f.Files)
            {
                string dir =
                    pf.RelDir.Length == 0
                        ? f.TargetFolder
                        : Path.Combine(
                            f.TargetFolder,
                            pf.RelDir);

                var it = new DownloadItem
                {
                    Url = pf.Url,
                    Folder = dir,
                    FileName =
                        UniqueName(
                            dir,
                            DownloadEngine.Sanitize(
                                pf.Name)),
                    TotalBytes = pf.Size,
                    NeedsProbe =
                        DownloadEngine.IsHttp(pf.Url),
                    State =
                        startNow
                            ? DlState.Queued
                            : DlState.Paused
                };

                items.Add(it);
                selectNewId = it.Id;

                localPaths.Add(
                    Path.Combine(
                        pf.RelDir,
                        it.FileName));
            }

            MarkChanged();
            SaveState();

            if (f.SaveLocalPlaylist)
                WriteLocalPlaylist(
                    f.TargetFolder,
                    localPaths);
        }

        // ডাউনলোড ফোল্ডারের ভেতর <ফোল্ডারের নাম>.m3u8 (রিলেটিভ পাথ, ফাইল নামলেই VLC-তে চলবে)
        void WriteLocalPlaylist(
            string targetFolder,
            List<string> relPaths)
        {
            try
            {
                var result =
                    PlaylistBuilder.BuildLocal(
                        relPaths);

                if (result.Count == 0)
                {
                    lblActive.Text =
                        "No video or audio files selected: local playlist not created.";
                    return;
                }

                Directory.CreateDirectory(
                    targetFolder);

                string leaf =
                    DownloadEngine.Sanitize(
                        Path.GetFileName(
                            targetFolder.TrimEnd(
                                '\\',
                                '/')));

                if (string.IsNullOrWhiteSpace(leaf))
                    leaf = "playlist";

                string file =
                    Path.Combine(
                        targetFolder,
                        leaf + ".m3u8");

                PlaylistBuilder.Save(
                    file,
                    result.Text);

                lblActive.Text =
                    "Local playlist saved: " +
                    file;
            }
            catch (Exception ex)
            {
                lblActive.Text =
                    "Could not save the local playlist: " +
                    ex.Message;
            }
        }

        // এক্সটেনশনের "Create playlist": Add ডায়ালগ ছাড়া সরাসরি ফোল্ডার উইন্ডো (প্লেলিস্ট মোডে)
        async void OpenFolderForPlaylist(
            string url)
        {
            try
            {
                await addGate.WaitAsync();

                try
                {
                    if (!Uri.TryCreate(
                            url,
                            UriKind.Absolute,
                            out var u))
                        return;

                    Uri folder = u;

                    if (DownloadEngine.IsHttp(url))
                    {
                        using var cts =
                            new CancellationTokenSource(
                                TimeSpan.FromSeconds(20));

                        var (isFolder, resolved) =
                            await DownloadEngine.DetectFolderAsync(
                                u,
                                cts.Token);

                        if (!isFolder)
                        {
                            MessageBox.Show(
                                this,
                                "This link does not look like a folder (directory listing), so a playlist cannot be made from it.",
                                "Playlist",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);

                            return;
                        }

                        folder = resolved;
                    }
                    else if (!u.AbsolutePath.EndsWith("/"))
                    {
                        folder =
                            new Uri(
                                u.GetLeftPart(
                                    UriPartial.Path) +
                                "/");
                    }

                    AddFolder(
                        folder,
                        true,
                        true);
                }
                catch (Exception ex)
                {
                    lblActive.Text =
                        "Could not open the folder: " +
                        ex.Message;
                }
                finally
                {
                    addGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                AppLog.Write("MainForm.OpenFolderForPlaylist: cancelled");
            }
            catch (Exception exGuard)
            {
                Program.ReportError("MainForm.OpenFolderForPlaylist", exGuard);
            }
        }

        static bool IsHtmlName(
            string url)
        {
            try
            {
                string ext =
                    Path.GetExtension(
                        new Uri(url).AbsolutePath);

                return ext.Equals(
                           ".html",
                           StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(
                           ".htm",
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // "Suggest folders": শুধু ডিফল্ট ফোল্ডারে সেভ করলেই সাবফোল্ডার বসে (ইউজার নিজে ফোল্ডার বাছলে তার পছন্দই চূড়ান্ত)
        string SmartFolder(
            string folder,
            string fileName,
            string url)
        {
            if (!settings.SuggestByType &&
                !settings.SuggestByHost)
                return folder;

            try
            {
                string a =
                    Path.GetFullPath(
                        folder).TrimEnd('\\', '/');

                string b =
                    Path.GetFullPath(
                        settings.DefaultFolder).TrimEnd('\\', '/');

                if (!a.Equals(
                        b,
                        StringComparison.OrdinalIgnoreCase))
                    return folder;

                string f = folder;

                if (settings.SuggestByType)
                    f = Path.Combine(
                        f,
                        FileCategories.Of(
                            fileName));

                if (settings.SuggestByHost &&
                    Uri.TryCreate(
                        url,
                        UriKind.Absolute,
                        out var u) &&
                    u.Host.Length > 0)
                    f = Path.Combine(
                        f,
                        DownloadEngine.Sanitize(
                            u.Host));

                return f;
            }
            catch
            {
                return folder;
            }
        }

        string UniqueName(
            string folder,
            string name)
        {
            string stem =
                Path.GetFileNameWithoutExtension(
                    name);

            string ext =
                Path.GetExtension(name);

            string cand = name;
            int n = 1;

            // একই নামের ফাইল থাকলে Preferences অনুযায়ী: Rename / Overwrite / Ask
            bool replaceExisting = false;

            if (File.Exists(
                    Path.Combine(
                        folder,
                        name)))
            {
                if (settings.FileExists ==
                    FileExistsAction.Overwrite)
                {
                    replaceExisting = true;
                }
                else if (settings.FileExists ==
                         FileExistsAction.Ask)
                {
                    replaceExisting =
                        MessageBox.Show(
                            this,
                            "\"" + name + "\" already exists in this folder.\n\n" +
                            "Yes = replace it\n" +
                            "No = keep both (the new file gets a different name)",
                            "File exists",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question) ==
                        DialogResult.Yes;
                }
            }

            while (
                (File.Exists(
                    Path.Combine(
                        folder,
                        cand)) &&
                 !(replaceExisting &&
                   cand == name)) ||

                File.Exists(
                    Path.Combine(
                        folder,
                        cand) +
                    ".part") ||

                items.Any(i =>
                    i.Folder == folder &&
                    i.FileName.Equals(
                        cand,
                        StringComparison.OrdinalIgnoreCase)))
            {
                cand =
                    stem +
                    " (" +
                    (n++) +
                    ")" +
                    ext;
            }

            return cand;
        }
    }
}
