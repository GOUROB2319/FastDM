using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FastDM
{
    public class PickedFile
    {
        public string Url = string.Empty;
        public string Name = string.Empty;
        public long Size;
        public string RelDir = "";      // নির্বাচিত ফোল্ডারের ভেতরের রিলেটিভ পাথ
    }

    // ====================== লগইন ডায়ালগ ======================
    class CredentialForm : Form
    {
        readonly Uri uri;
        readonly TextBox txtUser, txtPass;
        readonly CheckBox chkRemember;

        public static bool Prompt(IWin32Window owner, Uri uri)
        {
            using var f = new CredentialForm(uri);
            Theme.Apply(f);
            return f.ShowDialog(owner) == DialogResult.OK;
        }

        CredentialForm(Uri u)
        {
            Theme.ConfigureForm(this);
            uri = u;
            Text = "Login required";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(420, 252);
            MinimumSize = new Size(360, 240);
            Font = Theme.CreateFont(Theme.FontBody);

            Controls.Add(new Label
            {
                Text = "This server needs a username and password:\n" + u.Scheme + "://" + u.Host,
                Location = new Point(16, 14),
                Size = new Size(388, 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });

            Controls.Add(new Label { Text = "Username", Location = new Point(16, 62), AutoSize = true });
            txtUser = new TextBox { Location = new Point(16, 84), Size = new Size(388, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(txtUser);

            Controls.Add(new Label { Text = "Password", Location = new Point(16, 118), AutoSize = true });
            txtPass = new TextBox { Location = new Point(16, 140), Size = new Size(388, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true };
            Controls.Add(txtPass);

            chkRemember = new CheckBox
            {
                Text = "Remember (save securely in Windows Credential Manager)",
                Location = new Point(16, 176),
                AutoSize = true
            };
            Controls.Add(chkRemember);

            var ok = new Button { Text = "OK", Location = new Point(222, 205), Size = new Size(90, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var cancel = new Button { Text = "Cancel", Location = new Point(318, 205), Size = new Size(90, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                if (txtUser.Text.Trim().Length == 0)
                {
                    ThemeDialog.Show(this, "Please enter a username.");
                    return;
                }
                bool remembered = CredStore.Set(uri, txtUser.Text.Trim(), txtPass.Text, chkRemember.Checked);
                if (chkRemember.Checked && !remembered)
                    ThemeDialog.Show(this,
                        "Windows Credential Manager could not save this login. The credentials will be available only until FastDM closes.",
                        "Login was not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.OK;
            };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    // ====================== ফোল্ডার ডাউনলোড ডায়ালগ ======================
    class FolderDownloadForm : Form
    {
        readonly Uri rootUri;
        Uri currentUri;
        readonly TextBox txtName, txtLocation, txtFilter;
        readonly NumericUpDown numSim;
        readonly TreeView tree;
        readonly Label lblStatus;
        readonly Button btnOk, btnStream, btnBack, btnUp;
        readonly Label lblPath;
        readonly Stack<Uri> history = new Stack<Uri>();
        readonly CheckBox chkLocalPlaylist;
        CancellationTokenSource? cts;
        bool suppress;

        public string TargetFolder { get; private set; } = string.Empty;
        public int Simultaneous => (int)numSim.Value;
        public List<PickedFile> Files { get; private set; } = new List<PickedFile>();

        // ডাউনলোডের সাথে ফোল্ডারে লোকাল .m3u8 বানাতে হবে কি না
        public bool SaveLocalPlaylist => chkLocalPlaylist.Checked;

        // playlistMode = এক্সটেনশনের "Create playlist" থেকে খোলা: প্লেলিস্ট বাটনই মূল (Enter চাপলে সেটাই চলে)
        public FolderDownloadForm(Uri folderUri, string defaultLocation, int simultaneous, bool playlistMode = false)
        {
            Theme.ConfigureForm(this);
            rootUri = folderUri;
            currentUri = folderUri;

            Text = playlistMode ? "Create Playlist" : "Download Folder";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(660, 676);
            MinimumSize = new Size(560, 520);
            Font = Theme.CreateFont(Theme.FontBody);

            // লিঙ্ক
            Controls.Add(new Label { Text = "Folder link", Location = new Point(16, 12), AutoSize = true });
            Controls.Add(new TextBox
            {
                Location = new Point(16, 32),
                Size = new Size(628, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                Text = AppLog.Redact(Uri.UnescapeDataString(rootUri.GetLeftPart(UriPartial.Path)))
            });

            // ফোল্ডারের নাম + একসাথে কয়টা ফাইল
            Controls.Add(new Label { Text = "Folder name", Location = new Point(16, 66), AutoSize = true });
            txtName = new TextBox { Location = new Point(16, 86), Size = new Size(320, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = DefaultName() };
            Controls.Add(txtName);

            Controls.Add(new Label { Text = "Simultaneous files", Location = new Point(352, 66), AutoSize = true });
            numSim = new NumericUpDown
            {
                Location = new Point(352, 86),
                Width = 120,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Minimum = 1,
                Maximum = 10,
                Value = Math.Clamp(simultaneous, 1, 10)
            };
            Controls.Add(numSim);

            // সেভ লোকেশন
            Controls.Add(new Label { Text = "Save to", Location = new Point(16, 120), AutoSize = true });
            txtLocation = new TextBox { Location = new Point(16, 140), Size = new Size(528, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = defaultLocation };
            Controls.Add(txtLocation);
            var browse = new Button { Text = "Browse…", Location = new Point(552, 139), Size = new Size(92, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            browse.Click += (s, e) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtLocation.Text };
                if (fb.ShowDialog(this) == DialogResult.OK) txtLocation.Text = fb.SelectedPath;
            };
            Controls.Add(browse);

            // এক্সটেনশন ফিল্টার
            Controls.Add(new Label
            {
                Text = "Only these extensions (e.g. mkv, mp4) — leave empty for all files",
                Location = new Point(16, 176),
                AutoSize = true
            });
            txtFilter = new TextBox { Location = new Point(16, 196), Size = new Size(628, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            txtFilter.TextChanged += (s, e) => ApplyFilter();
            Controls.Add(txtFilter);

            btnBack = new Button { Text = "← Back", Location = new Point(16, 228), Size = new Size(82, 27), Enabled = false };
            btnBack.Click += async (s, e) =>
            {
                if (history.Count == 0) return;
                Uri previous = history.Pop();
                await NavigateAsync(previous, rememberCurrent: false);
            };
            btnUp = new Button { Text = "↑ Up", Location = new Point(104, 228), Size = new Size(70, 27) };
            btnUp.Click += async (s, e) =>
            {
                Uri? parent = ParentUri(currentUri);
                if (parent != null) await NavigateAsync(parent, rememberCurrent: true);
            };
            lblPath = new Label { Location = new Point(184, 231), Size = new Size(460, 22), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, AutoEllipsis = true, Text = DisplayPath(currentUri) };
            Controls.Add(btnBack);
            Controls.Add(btnUp);
            Controls.Add(lblPath);

            // ট্রি ভিউ
            tree = new TreeView
            {
                Location = new Point(16, 260),
                Size = new Size(628, 272),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                CheckBoxes = true,
                HideSelection = false,
                ShowLines = true
            };
            tree.AfterCheck += OnAfterCheck;
            tree.NodeMouseDoubleClick += async (s, e) =>
            {
                if (e.Node?.Tag is RemoteNode node && node.IsDir && Uri.TryCreate(node.Url, UriKind.Absolute, out var target))
                    await NavigateAsync(target, rememberCurrent: true);
            };
            tree.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && tree.SelectedNode?.Tag is RemoteNode node && node.IsDir &&
                    Uri.TryCreate(node.Url, UriKind.Absolute, out var target))
                {
                    e.Handled = true;
                    await NavigateAsync(target, rememberCurrent: true);
                }
            };
            Controls.Add(tree);

            var all = new Button { Text = "Select all", Location = new Point(16, 540), Size = new Size(100, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            var none = new Button { Text = "Select none", Location = new Point(122, 540), Size = new Size(100, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            all.Click += (s, e) => { txtFilter.Text = ""; SetAll(true); };
            none.Click += (s, e) => SetAll(false);
            Controls.Add(all); Controls.Add(none);

            lblStatus = new Label
            {
                Location = new Point(236, 546),
                Size = new Size(408, 22),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Text = "Scanning…"
            };
            Controls.Add(lblStatus);

            // প্লেলিস্ট (VLC)
            chkLocalPlaylist = new CheckBox
            {
                Text = "Also create a local playlist (.m3u8) in the download folder",
                Location = new Point(16, 578),
                Size = new Size(628, 24),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Checked = false
            };
            Controls.Add(chkLocalPlaylist);

            btnStream = new Button
            {
                Text = "Save stream playlist…",
                Location = new Point(16, 628),
                Size = new Size(190, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Enabled = false
            };
            btnStream.Click += OnSaveStreamPlaylist;
            Controls.Add(btnStream);

            btnOk = new Button { Text = "Download", Location = new Point(444, 628), Size = new Size(110, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Enabled = false };
            var cancel = new Button { Text = "Cancel", Location = new Point(560, 628), Size = new Size(84, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            btnOk.Click += OnOk;
            Controls.Add(btnOk); Controls.Add(cancel);
            AcceptButton = playlistMode ? btnStream : btnOk;
            CancelButton = cancel;

            Shown += async (s, e) =>
            {
                try
                {
                    await ListCurrentAsync();
                }
                catch (OperationCanceledException)
                {
                    AppLog.Write("FolderDownloadForm.ctor Shown: cancelled");
                }
                catch (Exception exGuard)
                {
                    Program.ReportError("FolderDownloadForm.ctor Shown", exGuard);
                }
            };
            FormClosing += (s, e) => cts?.Cancel();
        }

        string DefaultName()
        {
            string p = Uri.UnescapeDataString(rootUri.AbsolutePath).TrimEnd('/');
            string n = p.Substring(p.LastIndexOf('/') + 1);
            return n.Length > 0 ? SafeSeg(n) : SafeSeg(rootUri.Host);
        }

        // ফোল্ডার/ফাইলের নামে যেসব অক্ষর উইন্ডোজে চলে না সেগুলো বাদ
        static string SafeSeg(string s)
        {
            string r = DownloadEngine.Sanitize(s ?? "").TrimEnd('.', ' ');
            return r.Length == 0 ? "_" : r;
        }

        // ---------- স্ক্যান ----------
        async Task<RemoteNode> ListWithCredentialsAsync(Uri target, CancellationToken token)
        {
            while (true)
            {
                try
                {
                    var progress = new Progress<int>(n => lblStatus.Text = "Loading… " + n + " file(s) found");
                    return await RemoteLister.ListDirectoryAsync(target, progress, token);
                }
                catch (AuthRequiredException)
                {
                    if (!CredentialForm.Prompt(this, target)) throw new OperationCanceledException(token);
                }
            }
        }

        async Task<RemoteNode> ScanWithCredentialsAsync(Uri target, IProgress<int> progress, CancellationToken token)
        {
            while (true)
            {
                try { return await RemoteLister.ScanAsync(target, progress, token); }
                catch (AuthRequiredException)
                {
                    if (!CredentialForm.Prompt(this, target)) throw new OperationCanceledException(token);
                }
            }
        }

        async Task ListCurrentAsync()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
            btnOk.Enabled = false;
            btnStream.Enabled = false;
            btnBack.Enabled = history.Count > 0;
            btnUp.Enabled = ParentUri(currentUri) != null;
            try
            {
                lblStatus.Text = "Loading directory…";
                BuildTree(await ListWithCredentialsAsync(currentUri, cts.Token));
                lblPath.Text = DisplayPath(currentUri);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { lblStatus.Text = "Could not open directory: " + AppLog.Redact(ex.Message); }
        }

        async Task NavigateAsync(Uri target, bool rememberCurrent)
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
            btnOk.Enabled = false;
            btnStream.Enabled = false;
            try
            {
                lblStatus.Text = "Loading directory…";
                var listing = await ListWithCredentialsAsync(target, cts.Token);
                if (rememberCurrent) history.Push(currentUri);
                currentUri = target;
                BuildTree(listing);
                lblPath.Text = DisplayPath(currentUri);
                btnBack.Enabled = history.Count > 0;
                btnUp.Enabled = ParentUri(currentUri) != null;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { lblStatus.Text = "Could not open directory: " + AppLog.Redact(ex.Message); }
        }

        static string DisplayPath(Uri target) => Uri.UnescapeDataString(target.AbsolutePath);

        static Uri? ParentUri(Uri target)
        {
            string path = Uri.UnescapeDataString(target.AbsolutePath).TrimEnd('/');
            if (path.Length == 0) return null;
            int slash = path.LastIndexOf('/');
            string parent = slash <= 0 ? "/" : path.Substring(0, slash + 1);
            var builder = new UriBuilder(target) { Path = parent, Fragment = "" };
            Uri result = builder.Uri;
            return result.AbsoluteUri.Equals(target.AbsoluteUri, StringComparison.OrdinalIgnoreCase) ? null : result;
        }

        void BuildTree(RemoteNode root)
        {
            suppress = true;
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                bool hasMedia = EnumerateRemote(root).Any(x => !x.IsDir && PlaylistBuilder.IsMedia(x.Name));
                AddChildren(tree.Nodes, root, hasMedia);
                foreach (TreeNode n in tree.Nodes) if (n.Nodes.Count > 0) n.Expand();
            }
            finally
            {
                tree.EndUpdate();
                suppress = false;
            }

            if (tree.Nodes.Count == 0)
            {
                lblStatus.Text = "No files found. The page may not be a plain directory listing.";
                return;
            }
            if (txtFilter.Text.Trim().Length > 0) ApplyFilter(); else UpdateSummary();
        }

        static IEnumerable<RemoteNode> EnumerateRemote(RemoteNode node)
        {
            foreach (var child in node.Children)
            {
                yield return child;
                if (child.IsDir)
                    foreach (var descendant in EnumerateRemote(child)) yield return descendant;
            }
        }

        void AddChildren(TreeNodeCollection col, RemoteNode n, bool hasMedia)
        {
            foreach (var c in n.Children
                .OrderByDescending(x => x.IsDir)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                string text = c.IsDir ? c.Name
                            : c.Size > 0 ? c.Name + "   (" + Fmt(c.Size) + ")" : c.Name;
                var tn = new TreeNode(text) { Tag = c };
                col.Add(tn);
                if (c.IsDir)
                {
                    AddChildren(tn.Nodes, c, hasMedia);
                    tn.Checked = tn.Nodes.Count == 0 ? !hasMedia : tn.Nodes.Cast<TreeNode>().All(x => x.Checked);
                }
                else
                {
                    tn.Checked = !hasMedia || PlaylistBuilder.IsMedia(c.Name);
                }
            }
        }

        // ---------- চেক লজিক ----------
        void OnAfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (suppress || e.Action == TreeViewAction.Unknown) return;
            suppress = true;
            try
            {
                var node = e.Node;
                if (node == null) return;
                SetChildren(node, node.Checked);
                for (var p = node.Parent; p != null; p = p.Parent)
                    p.Checked = p.Nodes.Count > 0 && p.Nodes.Cast<TreeNode>().All(x => x.Checked);
            }
            finally { suppress = false; }
            UpdateSummary();
        }

        static void SetChildren(TreeNode n, bool on)
        {
            foreach (TreeNode c in n.Nodes) { c.Checked = on; SetChildren(c, on); }
        }

        void SetAll(bool on)
        {
            suppress = true;
            try
            {
                foreach (TreeNode n in tree.Nodes) { n.Checked = on; SetChildren(n, on); }
            }
            finally { suppress = false; }
            UpdateSummary();
        }

        void ApplyFilter()
        {
            if (tree.Nodes.Count == 0) return;
            var exts = txtFilter.Text
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => "." + x.Trim().TrimStart('.').ToLowerInvariant())
                .ToHashSet();

            suppress = true;
            try
            {
                foreach (TreeNode n in tree.Nodes) ApplyFilterNode(n, exts);
            }
            finally { suppress = false; }
            UpdateSummary();
        }

        static bool ApplyFilterNode(TreeNode n, HashSet<string> exts)
        {
            if (n.Tag is not RemoteNode rn) return false;
            if (!rn.IsDir)
            {
                bool on = exts.Count == 0 || exts.Contains(Path.GetExtension(rn.Name).ToLowerInvariant());
                n.Checked = on;
                return on;
            }
            foreach (TreeNode c in n.Nodes) ApplyFilterNode(c, exts);
            n.Checked = n.Nodes.Count == 0 ? exts.Count == 0 : n.Nodes.Cast<TreeNode>().All(x => x.Checked);
            return n.Checked;
        }

        void UpdateSummary()
        {
            int files = 0, folders = 0; long size = 0; bool unknown = false;
            void Count(TreeNode node)
            {
                if (node.Tag is not RemoteNode remote) return;
                if (node.Checked && remote.IsDir) folders++;
                if (node.Checked && !remote.IsDir)
                {
                    files++;
                    if (remote.Size > 0) size += remote.Size; else unknown = true;
                }
                else if (node.Checked && remote.IsDir && node.Nodes.Count == 0)
                {
                    unknown = true;
                }
                foreach (TreeNode child in node.Nodes) Count(child);
            }
            foreach (TreeNode node in tree.Nodes) Count(node);

            string t = files + " file(s), " + folders + " folder(s) selected";
            if (size > 0) t += " — " + Fmt(size) + (unknown ? "+" : "");
            lblStatus.Text = t;
            btnOk.Enabled = files > 0 || folders > 0;
            btnStream.Enabled = files > 0 || folders > 0;
        }

        // ---------- OK ----------
        async void OnOk(object? sender, EventArgs e)
        {
            string loc = txtLocation.Text.Trim();
            if (loc.Length == 0)
            {
                ThemeDialog.Show(this, "Please choose a save location.");
                return;
            }

            string name = txtName.Text.Trim();
            TargetFolder = name.Length == 0 ? loc : Path.Combine(loc, SafeSeg(name));

            btnOk.Enabled = false;
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
            var list = new List<PickedFile>();
            try
            {
                await CollectSelectedAsync(list, cts.Token);
            }
            catch (OperationCanceledException) { btnOk.Enabled = true; return; }
            catch (Exception ex)
            {
                lblStatus.Text = "Could not scan selected folder: " + AppLog.Redact(ex.Message);
                btnOk.Enabled = true;
                return;
            }
            if (list.Count == 0)
            {
                ThemeDialog.Show(this, "Select at least one file.");
                UpdateSummary();
                return;
            }
            Files = list;
            DialogResult = DialogResult.OK;
        }

        async Task CollectSelectedAsync(List<PickedFile> files, CancellationToken token)
        {
            string relative = RelativePath(rootUri, currentUri);
            var filter = ParseFilter();
            int selectedFolders = CountSelectedFolders(tree.Nodes);
            int completed = 0;
            foreach (TreeNode n in tree.Nodes)
                await CollectSelectedNodeAsync(n, relative, files, filter, selectedFolders, () => ++completed, token);
        }

        static int CountSelectedFolders(TreeNodeCollection nodes)
        {
            int count = 0;
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is RemoteNode { IsDir: true } && node.Checked) count++;
                else count += CountSelectedFolders(node.Nodes);
            }
            return count;
        }

        async Task CollectSelectedNodeAsync(TreeNode node, string relative, List<PickedFile> files,
            HashSet<string> filter, int selectedFolders, Func<int> nextFolder, CancellationToken token)
        {
            if (node.Tag is not RemoteNode item) return;
            token.ThrowIfCancellationRequested();
            if (!item.IsDir)
            {
                if (node.Checked && MatchesFilter(item.Name, filter))
                    files.Add(new PickedFile { Url = item.Url, Name = item.Name, Size = item.Size, RelDir = relative });
                return;
            }

            string nestedRelative = Path.Combine(relative, SafeSeg(item.Name));
            if (node.Checked)
            {
                lblStatus.Text = "Scanning selected folder " + nextFolder() + " of " + selectedFolders + "…";
                var progress = new Progress<int>(count => lblStatus.Text = "Scanning selected folder… " + count + " file(s) found");
                var subtree = await ScanWithCredentialsAsync(new Uri(item.Url), progress, token);
                CollectRemote(subtree, nestedRelative, files, filter);
                return;
            }

            foreach (TreeNode child in node.Nodes)
                await CollectSelectedNodeAsync(child, nestedRelative, files, filter, selectedFolders, nextFolder, token);
        }

        HashSet<string> ParseFilter() => txtFilter.Text
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => "." + x.Trim().TrimStart('.').ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        static bool MatchesFilter(string name, HashSet<string> filter) =>
            filter.Count == 0 || filter.Contains(Path.GetExtension(name));

        static string RelativePath(Uri root, Uri current)
        {
            string basePath = Uri.UnescapeDataString(root.AbsolutePath).TrimEnd('/');
            string path = Uri.UnescapeDataString(current.AbsolutePath).TrimEnd('/');
            if (path.Equals(basePath, StringComparison.OrdinalIgnoreCase)) return "";
            string prefix = basePath.Length == 0 ? "/" : basePath + "/";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? string.Join(Path.DirectorySeparatorChar.ToString(), path.Substring(prefix.Length).Split('/').Select(SafeSeg))
                : "";
        }

        static void CollectRemote(RemoteNode dir, string rel, List<PickedFile> files, HashSet<string> filter)
        {
            foreach (var child in dir.Children)
            {
                if (child.IsDir) CollectRemote(child, Path.Combine(rel, SafeSeg(child.Name)), files, filter);
                else if (MatchesFilter(child.Name, filter)) files.Add(new PickedFile { Url = child.Url, Name = child.Name, Size = child.Size, RelDir = rel });
            }
        }

        // ---------- স্ট্রিম প্লেলিস্ট (সার্ভারের লিঙ্ক, ডাউনলোড ছাড়াই VLC-তে চলে) ----------
        async void OnSaveStreamPlaylist(object? sender, EventArgs e)
        {
            var list = new List<PickedFile>();
            btnStream.Enabled = false;
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
            try { await CollectSelectedAsync(list, cts.Token); }
            catch (OperationCanceledException) { btnStream.Enabled = true; return; }
            catch (Exception ex)
            {
                lblStatus.Text = "Could not scan selected folder: " + AppLog.Redact(ex.Message);
                UpdateSummary();
                return;
            }
            UpdateSummary();

            var result = PlaylistBuilder.BuildStream(list);
            if (result.Count == 0)
            {
                ThemeDialog.Show(this, "No video or audio files are selected, so there is nothing to put in a playlist.");
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Title = "Save playlist",
                Filter = "M3U8 playlist (*.m3u8)|*.m3u8",
                DefaultExt = "m3u8",
                FileName = SafeSeg(txtName.Text.Trim().Length > 0 ? txtName.Text.Trim() : DefaultName()) + ".m3u8"
            };

            string dir = txtLocation.Text.Trim();
            if (dir.Length > 0 && Directory.Exists(dir)) sfd.InitialDirectory = dir;

            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                PlaylistBuilder.Save(sfd.FileName, result.Text);
                lblStatus.Text = "Playlist saved: " + result.Count + " file(s)" +
                                 (result.Skipped > 0 ? ", " + result.Skipped + " skipped (not video/audio)" : "");
            }
            catch (Exception ex)
            {
                ThemeDialog.ShowError(this, "Playlist", "Couldn't save the playlist. Check the save location and try again.", ex);
            }
        }

        static string Fmt(double b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (i == 0 ? b.ToString("0") : b.ToString("0.##")) + " " + u[i];
        }
    }
}
