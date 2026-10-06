#nullable disable
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
        public string Url;
        public string Name;
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
            uri = u;
            Text = "Login required";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(420, 252);
            Font = new Font("Segoe UI", 9.5f);

            Controls.Add(new Label
            {
                Text = "This server needs a username and password:\n" + u.Scheme + "://" + u.Host,
                Location = new Point(16, 14),
                Size = new Size(388, 40)
            });

            Controls.Add(new Label { Text = "Username", Location = new Point(16, 62), AutoSize = true });
            txtUser = new TextBox { Location = new Point(16, 84), Size = new Size(388, 26) };
            Controls.Add(txtUser);

            Controls.Add(new Label { Text = "Password", Location = new Point(16, 118), AutoSize = true });
            txtPass = new TextBox { Location = new Point(16, 140), Size = new Size(388, 26), UseSystemPasswordChar = true };
            Controls.Add(txtPass);

            chkRemember = new CheckBox
            {
                Text = "Remember (save securely in Windows Credential Manager)",
                Location = new Point(16, 176),
                AutoSize = true
            };
            Controls.Add(chkRemember);

            var ok = new Button { Text = "OK", Location = new Point(222, 205), Size = new Size(90, 34) };
            var cancel = new Button { Text = "Cancel", Location = new Point(318, 205), Size = new Size(90, 34), DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                if (txtUser.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, "Please enter a username.");
                    return;
                }
                CredStore.Set(uri, txtUser.Text.Trim(), txtPass.Text, chkRemember.Checked);
                DialogResult = DialogResult.OK;
            };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    // ====================== ফোল্ডার ডাউনলোড ডায়ালগ ======================
    class FolderDownloadForm : Form
    {
        readonly Uri uri;
        readonly TextBox txtName, txtLocation, txtFilter;
        readonly NumericUpDown numSim;
        readonly TreeView tree;
        readonly Label lblStatus;
        readonly Button btnOk, btnStream;
        readonly CheckBox chkLocalPlaylist;
        CancellationTokenSource cts;
        bool suppress;

        public string TargetFolder { get; private set; }
        public int Simultaneous => (int)numSim.Value;
        public List<PickedFile> Files { get; private set; } = new List<PickedFile>();

        // ডাউনলোডের সাথে ফোল্ডারে লোকাল .m3u8 বানাতে হবে কি না
        public bool SaveLocalPlaylist => chkLocalPlaylist.Checked;

        // playlistMode = এক্সটেনশনের "Create playlist" থেকে খোলা: প্লেলিস্ট বাটনই মূল (Enter চাপলে সেটাই চলে)
        public FolderDownloadForm(Uri folderUri, string defaultLocation, int simultaneous, bool playlistMode = false)
        {
            uri = folderUri;

            Text = playlistMode ? "Create Playlist" : "Download Folder";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(660, 676);
            Font = new Font("Segoe UI", 9.5f);

            // লিঙ্ক
            Controls.Add(new Label { Text = "Folder link", Location = new Point(16, 12), AutoSize = true });
            Controls.Add(new TextBox
            {
                Location = new Point(16, 32),
                Size = new Size(628, 26),
                ReadOnly = true,
                Text = Uri.UnescapeDataString(uri.GetLeftPart(UriPartial.Path))
            });

            // ফোল্ডারের নাম + একসাথে কয়টা ফাইল
            Controls.Add(new Label { Text = "Folder name", Location = new Point(16, 66), AutoSize = true });
            txtName = new TextBox { Location = new Point(16, 86), Size = new Size(320, 26), Text = DefaultName() };
            Controls.Add(txtName);

            Controls.Add(new Label { Text = "Simultaneous files", Location = new Point(352, 66), AutoSize = true });
            numSim = new NumericUpDown
            {
                Location = new Point(352, 86),
                Width = 120,
                Minimum = 1,
                Maximum = 10,
                Value = Math.Clamp(simultaneous, 1, 10)
            };
            Controls.Add(numSim);

            // সেভ লোকেশন
            Controls.Add(new Label { Text = "Save to", Location = new Point(16, 120), AutoSize = true });
            txtLocation = new TextBox { Location = new Point(16, 140), Size = new Size(528, 26), Text = defaultLocation };
            Controls.Add(txtLocation);
            var browse = new Button { Text = "Browse…", Location = new Point(552, 139), Size = new Size(92, 28) };
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
            txtFilter = new TextBox { Location = new Point(16, 196), Size = new Size(628, 26) };
            txtFilter.TextChanged += (s, e) => ApplyFilter();
            Controls.Add(txtFilter);

            // ট্রি ভিউ
            tree = new TreeView
            {
                Location = new Point(16, 232),
                Size = new Size(628, 300),
                CheckBoxes = true,
                HideSelection = false,
                ShowLines = true
            };
            tree.AfterCheck += OnAfterCheck;
            Controls.Add(tree);

            var all = new Button { Text = "Select all", Location = new Point(16, 540), Size = new Size(100, 28) };
            var none = new Button { Text = "Select none", Location = new Point(122, 540), Size = new Size(100, 28) };
            all.Click += (s, e) => { txtFilter.Text = ""; SetAll(true); };
            none.Click += (s, e) => SetAll(false);
            Controls.Add(all); Controls.Add(none);

            lblStatus = new Label
            {
                Location = new Point(236, 546),
                Size = new Size(408, 22),
                Text = "Scanning…"
            };
            Controls.Add(lblStatus);

            // প্লেলিস্ট (VLC)
            chkLocalPlaylist = new CheckBox
            {
                Text = "Also create a local playlist (.m3u8) in the download folder",
                Location = new Point(16, 578),
                Size = new Size(628, 24),
                Checked = false
            };
            Controls.Add(chkLocalPlaylist);

            btnStream = new Button
            {
                Text = "Save stream playlist…",
                Location = new Point(16, 628),
                Size = new Size(190, 34),
                Enabled = false
            };
            btnStream.Click += OnSaveStreamPlaylist;
            Controls.Add(btnStream);

            btnOk = new Button { Text = "Download", Location = new Point(444, 628), Size = new Size(110, 34), Enabled = false };
            var cancel = new Button { Text = "Cancel", Location = new Point(560, 628), Size = new Size(84, 34), DialogResult = DialogResult.Cancel };
            btnOk.Click += OnOk;
            Controls.Add(btnOk); Controls.Add(cancel);
            AcceptButton = playlistMode ? btnStream : btnOk;
            CancelButton = cancel;

            Shown += async (s, e) => await ScanAsync();
            FormClosing += (s, e) => cts?.Cancel();
        }

        string DefaultName()
        {
            string p = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
            string n = p.Substring(p.LastIndexOf('/') + 1);
            return n.Length > 0 ? SafeSeg(n) : SafeSeg(uri.Host);
        }

        // ফোল্ডার/ফাইলের নামে যেসব অক্ষর উইন্ডোজে চলে না সেগুলো বাদ
        static string SafeSeg(string s)
        {
            string r = Engine.Sanitize(s ?? "").TrimEnd('.', ' ');
            return r.Length == 0 ? "_" : r;
        }

        // ---------- স্ক্যান ----------
        async Task ScanAsync()
        {
            cts = new CancellationTokenSource();
            btnOk.Enabled = false;
            btnStream.Enabled = false;

            while (true)
            {
                try
                {
                    lblStatus.Text = "Scanning…";
                    var prog = new Progress<int>(n => lblStatus.Text = "Scanning… " + n + " file(s) found");
                    var root = await RemoteLister.ScanAsync(uri, prog, cts.Token);
                    BuildTree(root);
                    return;
                }
                catch (AuthRequiredException)
                {
                    if (!CredentialForm.Prompt(this, uri)) { lblStatus.Text = "Login cancelled."; return; }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    lblStatus.Text = "Scan failed: " + ex.Message;
                    return;
                }
            }
        }

        void BuildTree(RemoteNode root)
        {
            suppress = true;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            AddChildren(tree.Nodes, root);
            foreach (TreeNode n in tree.Nodes) if (n.Nodes.Count > 0) n.Expand();
            tree.EndUpdate();
            suppress = false;

            if (tree.Nodes.Count == 0)
            {
                lblStatus.Text = "No files found. The page may not be a plain directory listing.";
                return;
            }
            if (txtFilter.Text.Trim().Length > 0) ApplyFilter(); else UpdateSummary();
        }

        void AddChildren(TreeNodeCollection col, RemoteNode n)
        {
            foreach (var c in n.Children
                .OrderByDescending(x => x.IsDir)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                string text = c.IsDir ? c.Name
                            : c.Size > 0 ? c.Name + "   (" + Fmt(c.Size) + ")" : c.Name;
                var tn = new TreeNode(text) { Tag = c };
                col.Add(tn);
                tn.Checked = true;
                if (c.IsDir) AddChildren(tn.Nodes, c);
            }
        }

        // ---------- চেক লজিক ----------
        void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (suppress || e.Action == TreeViewAction.Unknown) return;
            suppress = true;
            SetChildren(e.Node, e.Node.Checked);
            for (var p = e.Node.Parent; p != null; p = p.Parent)
                p.Checked = p.Nodes.Cast<TreeNode>().Any(x => x.Checked);
            suppress = false;
            UpdateSummary();
        }

        static void SetChildren(TreeNode n, bool on)
        {
            foreach (TreeNode c in n.Nodes) { c.Checked = on; SetChildren(c, on); }
        }

        void SetAll(bool on)
        {
            suppress = true;
            foreach (TreeNode n in tree.Nodes) { n.Checked = on; SetChildren(n, on); }
            suppress = false;
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
            foreach (TreeNode n in tree.Nodes) ApplyFilterNode(n, exts);
            suppress = false;
            UpdateSummary();
        }

        static bool ApplyFilterNode(TreeNode n, HashSet<string> exts)
        {
            var rn = (RemoteNode)n.Tag;
            if (!rn.IsDir)
            {
                bool on = exts.Count == 0 || exts.Contains(Path.GetExtension(rn.Name).ToLowerInvariant());
                n.Checked = on;
                return on;
            }
            bool any = false;
            foreach (TreeNode c in n.Nodes) any |= ApplyFilterNode(c, exts);
            n.Checked = any || exts.Count == 0;
            return n.Checked;
        }

        void UpdateSummary()
        {
            int files = 0; long size = 0; bool unknown = false;
            Sum(tree.Nodes, ref files, ref size, ref unknown);

            string t = files + " file(s) selected";
            if (size > 0) t += " — " + Fmt(size) + (unknown ? "+" : "");
            lblStatus.Text = t;
            btnOk.Enabled = files > 0;
            btnStream.Enabled = files > 0;
        }

        static void Sum(TreeNodeCollection col, ref int files, ref long size, ref bool unknown)
        {
            foreach (TreeNode n in col)
            {
                if (!n.Checked) continue;
                var rn = (RemoteNode)n.Tag;
                if (rn.IsDir) Sum(n.Nodes, ref files, ref size, ref unknown);
                else
                {
                    files++;
                    if (rn.Size > 0) size += rn.Size; else unknown = true;
                }
            }
        }

        // ---------- OK ----------
        void OnOk(object sender, EventArgs e)
        {
            string loc = txtLocation.Text.Trim();
            if (loc.Length == 0)
            {
                MessageBox.Show(this, "Please choose a save location.");
                return;
            }

            string name = txtName.Text.Trim();
            TargetFolder = name.Length == 0 ? loc : Path.Combine(loc, SafeSeg(name));

            var list = new List<PickedFile>();
            Collect(tree.Nodes, "", list);
            if (list.Count == 0)
            {
                MessageBox.Show(this, "Select at least one file.");
                return;
            }
            Files = list;
            DialogResult = DialogResult.OK;
        }

        // ---------- স্ট্রিম প্লেলিস্ট (সার্ভারের লিঙ্ক, ডাউনলোড ছাড়াই VLC-তে চলে) ----------
        void OnSaveStreamPlaylist(object sender, EventArgs e)
        {
            var list = new List<PickedFile>();
            Collect(tree.Nodes, "", list);

            var result = PlaylistBuilder.BuildStream(list);
            if (result.Count == 0)
            {
                MessageBox.Show(this, "No video or audio files are selected, so there is nothing to put in a playlist.");
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
                MessageBox.Show(this, "Could not save the playlist.\n\n" + ex.Message, "Playlist", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        static void Collect(TreeNodeCollection col, string rel, List<PickedFile> list)
        {
            foreach (TreeNode n in col)
            {
                if (!n.Checked) continue;
                var rn = (RemoteNode)n.Tag;
                if (rn.IsDir) Collect(n.Nodes, Path.Combine(rel, SafeSeg(rn.Name)), list);
                else list.Add(new PickedFile { Url = rn.Url, Name = rn.Name, Size = rn.Size, RelDir = rel });
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
