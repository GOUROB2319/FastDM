using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        readonly TextBox txtName, txtLocation, txtFilter;
        readonly NumericUpDown numSim;
        readonly TreeView tree;
        readonly Label lblStatus;
        readonly Label lblSelectedSize;
        readonly Button btnOk, btnStream, btnCancelScan;
        readonly ImageList treeImages;
        readonly HashSet<TreeNode> partiallySelectedNodes = new HashSet<TreeNode>();
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

            // ট্রি ভিউ
            treeImages = CreateShellImageList();
            tree = new TreeView
            {
                Location = new Point(16, 228),
                Size = new Size(628, 304),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                CheckBoxes = true,
                HideSelection = false,
                ShowLines = true,
                DrawMode = TreeViewDrawMode.OwnerDrawText,
                ImageList = treeImages,
                AccessibleName = "Remote folders and files",
                AccessibleDescription = "Browse the remote folder tree. Use Space to select folders or files."
            };
            tree.AfterCheck += OnAfterCheck;
            tree.DrawNode += DrawFolderNode;
            Controls.Add(tree);

            var all = new Button { Text = "Select all", Location = new Point(16, 540), Size = new Size(100, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            var none = new Button { Text = "Select none", Location = new Point(122, 540), Size = new Size(100, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            var expand = new Button { Text = "Expand all", Location = new Point(228, 540), Size = new Size(94, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            var collapse = new Button { Text = "Collapse all", Location = new Point(328, 540), Size = new Size(94, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            all.Click += (s, e) => { txtFilter.Text = ""; SetAll(true); };
            none.Click += (s, e) => SetAll(false);
            expand.Click += (s, e) => tree.ExpandAll();
            collapse.Click += (s, e) => tree.CollapseAll();
            Controls.Add(all); Controls.Add(none); Controls.Add(expand); Controls.Add(collapse);

            lblStatus = new Label
            {
                Location = new Point(16, 546),
                Size = new Size(330, 22),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Text = "Scanning…",
                AutoEllipsis = true,
                AccessibleName = "Folder scan status",
                AccessibleDescription = "Shows directory loading and selection status."
            };
            Controls.Add(lblStatus);

            lblSelectedSize = new Label
            {
                Location = new Point(350, 546),
                Size = new Size(188, 22),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Text = "Selected size: 0 B",
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                AccessibleName = "Selected total size",
                AccessibleDescription = "Estimated total size of selected files."
            };
            Controls.Add(lblSelectedSize);

            btnCancelScan = new Button { Text = "Cancel scan", Location = new Point(540, 540), Size = new Size(104, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Visible = false };
            btnCancelScan.Click += (s, e) => cts?.Cancel();
            Controls.Add(btnCancelScan);

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
                    await ScanRootAsync();
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
            FormClosed += (s, e) =>
            {
                cts?.Dispose();
                treeImages.Dispose();
            };
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ShellFileInfo
        {
            public IntPtr Icon;
            public int IconIndex;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string? TypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShellFileInfo info, uint size, uint flags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr icon);

        ImageList CreateShellImageList()
        {
            var list = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(Theme.Scale(this, 16), Theme.Scale(this, 16))
            };
            AddShellImage(list, "folder", "folder", 0x10);
            AddShellImage(list, "file", ".txt", 0x80);
            return list;
        }

        static void AddShellImage(ImageList list, string key, string path, uint attributes)
        {
            const uint flags = 0x00000100 | 0x00000001 | 0x00000010; // icon, small icon, use attributes
            var info = new ShellFileInfo();
            IntPtr result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
            if (result == IntPtr.Zero || info.Icon == IntPtr.Zero)
            {
                using var fallback = SystemIcons.Application.ToBitmap();
                list.Images.Add(key, (Image)fallback.Clone());
                return;
            }

            try
            {
                using var icon = Icon.FromHandle(info.Icon);
                using var bitmap = icon.ToBitmap();
                list.Images.Add(key, (Image)bitmap.Clone());
            }
            finally { DestroyIcon(info.Icon); }
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

        async Task ScanRootAsync()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
            btnOk.Enabled = false;
            btnStream.Enabled = false;
            btnCancelScan.Visible = true;
            try
            {
                lblStatus.Text = "Scanning…";
                lblSelectedSize.Text = "Selected size: calculating…";
                var progress = new Progress<int>(n => lblStatus.Text = "Scanning… " + n.ToString("N0") + " items");
                var root = await ScanWithCredentialsAsync(rootUri, progress, cts.Token);
                BuildTree(root);
                if (root.IsTruncated)
                    lblStatus.Text = "Showing first " + RemoteLister.MaxNodes.ToString("N0") + " items";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { lblStatus.Text = "Could not scan folder: " + AppLog.Redact(ex.Message); lblSelectedSize.Text = "Selected size: unavailable"; }
            finally { btnCancelScan.Visible = false; }
        }

        void BuildTree(RemoteNode root)
        {
            suppress = true;
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                partiallySelectedNodes.Clear();
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
                lblSelectedSize.Text = "Selected size: 0 B";
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
                string imageKey = c.IsDir ? "folder" : "file";
                var tn = new TreeNode(text) { Tag = c, ImageKey = imageKey, SelectedImageKey = imageKey };
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

        void DrawFolderNode(object? sender, DrawTreeNodeEventArgs e)
        {
            TreeNode? node = e.Node;
            if (node == null || !partiallySelectedNodes.Contains(node))
            {
                e.DrawDefault = true;
                return;
            }

            e.DrawDefault = false;
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            string suffix = " (partial)";
            var bounds = e.Bounds;
            int availableWidth = Math.Max(0, tree.ClientRectangle.Right - bounds.X - 2);
            if (availableWidth == 0 || bounds.Height <= 0) return;

            int suffixWidth = TextRenderer.MeasureText(suffix, tree.Font,
                new Size(int.MaxValue, bounds.Height), flags).Width;
            suffixWidth = Math.Min(suffixWidth, availableWidth);
            int maximumTextWidth = Math.Max(0, availableWidth - suffixWidth);
            int measuredTextWidth = TextRenderer.MeasureText(node.Text, tree.Font,
                new Size(int.MaxValue, bounds.Height), flags).Width;
            int textWidth = Math.Min(measuredTextWidth, maximumTextWidth);
            int totalWidth = Math.Min(availableWidth, textWidth + suffixWidth);
            var displayBounds = new Rectangle(bounds.X, bounds.Y, totalWidth, bounds.Height);
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            bool focused = (e.State & TreeNodeStates.Focused) != 0;
            Color background = selected ? Theme.P.Accent : tree.BackColor;
            Color foreground = selected
                ? Theme.ContrastText(background)
                : node.ForeColor.IsEmpty ? Theme.P.Text : node.ForeColor;

            if (selected)
                using (var selectionBrush = new SolidBrush(background))
                    e.Graphics.FillRectangle(selectionBrush, displayBounds);

            var textBounds = new Rectangle(bounds.X, bounds.Y, textWidth, bounds.Height);
            var suffixBounds = new Rectangle(bounds.X + textWidth, bounds.Y, suffixWidth, bounds.Height);
            TextRenderer.DrawText(e.Graphics, node.Text, tree.Font, textBounds, foreground, flags | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, suffix, tree.Font, suffixBounds,
                selected ? foreground : Theme.P.SubText, flags);

            if (focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, displayBounds, foreground, background);
        }

        (int Selected, int Total) CountSelectionUnits(TreeNode node)
        {
            if (node.Nodes.Count == 0)
                return (node.Checked ? 1 : 0, 1);

            int selected = 0;
            int total = 0;
            foreach (TreeNode child in node.Nodes)
            {
                var childCounts = CountSelectionUnits(child);
                selected += childCounts.Selected;
                total += childCounts.Total;
            }

            if (node.Tag is RemoteNode { IsDir: true } && selected > 0 && selected < total)
                partiallySelectedNodes.Add(node);
            return (selected, total);
        }

        void RefreshPartialSelectionStates()
        {
            partiallySelectedNodes.Clear();
            foreach (TreeNode node in tree.Nodes) CountSelectionUnits(node);
            tree.Invalidate();
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
            RefreshPartialSelectionStates();
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
            lblStatus.Text = t;
            lblSelectedSize.Text = unknown
                ? size > 0 ? "Selected size: at least " + Fmt(size) : "Selected size: unknown"
                : "Selected size: " + Fmt(size);
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

        Task CollectSelectedAsync(List<PickedFile> files, CancellationToken token)
        {
            var filter = ParseFilter();
            foreach (TreeNode n in tree.Nodes)
                CollectSelectedTreeNode(n, "", files, filter, token);
            return Task.CompletedTask;
        }

        static void CollectSelectedTreeNode(TreeNode node, string relative, List<PickedFile> files,
            HashSet<string> filter, CancellationToken token)
        {
            if (node.Tag is not RemoteNode item) return;
            token.ThrowIfCancellationRequested();
            if (!item.IsDir)
            {
                if (node.Checked && MatchesFilter(item.Name, filter))
                    files.Add(new PickedFile { Url = item.Url, Name = item.Name, Size = item.Size, RelDir = relative });
                return;
            }

            string nestedRelative = FolderNamingPolicy.AppendRelative(relative, item.Name);
            foreach (TreeNode child in node.Nodes)
                CollectSelectedTreeNode(child, nestedRelative, files, filter, token);
        }

        HashSet<string> ParseFilter() => txtFilter.Text
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => "." + x.Trim().TrimStart('.').ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        static bool MatchesFilter(string name, HashSet<string> filter) =>
            filter.Count == 0 || filter.Contains(Path.GetExtension(name));

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
