namespace FastDM
{
    // ====================== Add URL ডায়ালগ ======================
    class AddUrlForm : Form
    {
        readonly TextBox txtUrls;
        readonly TextBox txtName;
        readonly TextBox txtFolder;

        public string[] Urls =>
            txtUrls.Lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

        public string FileNameText => txtName.Text.Trim();
        public string Folder => txtFolder.Text.Trim();

        public string RawText => txtUrls.Text;

        // True when the text came from a file: the import window checks every line, so no strict check here.
        public bool FromFile { get; private set; }

        public AddUrlForm(string initialUrl, string folder)
        {
            Text = "Add Download";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 336);
            Font = new Font("Segoe UI", 9.5f);

            Controls.Add(new Label
            {
                Text = "URL(s) — one link per line (file or folder)",
                Location = new Point(16, 12),
                AutoSize = true
            });

            txtUrls = new TextBox
            {
                Location = new Point(16, 34),
                Size = new Size(528, 110),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = initialUrl ?? ""
            };

            Controls.Add(txtUrls);

            var openFile = new Button
            {
                Text = "Open text file\u2026",
                Location = new Point(404, 6),
                Size = new Size(140, 26)
            };

            openFile.Click += (s, e) => LoadFromFile();
            Controls.Add(openFile);

            Controls.Add(new Label
            {
                Text = "File name (optional, single link only)",
                Location = new Point(16, 156),
                AutoSize = true
            });

            txtName = new TextBox
            {
                Location = new Point(16, 178),
                Size = new Size(528, 26)
            };

            Controls.Add(txtName);

            Controls.Add(new Label
            {
                Text = "Save to",
                Location = new Point(16, 214),
                AutoSize = true
            });

            txtFolder = new TextBox
            {
                Location = new Point(16, 236),
                Size = new Size(436, 26),
                Text = folder
            };

            Controls.Add(txtFolder);

            var browse = new Button
            {
                Text = "Browse…",
                Location = new Point(460, 235),
                Size = new Size(84, 28)
            };

            browse.Click += (s, e) =>
            {
                using var fb = new FolderBrowserDialog
                {
                    SelectedPath = txtFolder.Text
                };

                if (fb.ShowDialog(this) == DialogResult.OK)
                    txtFolder.Text = fb.SelectedPath;
            };

            Controls.Add(browse);

            var start = new Button
            {
                Text = "Start Download",
                Location = new Point(196, 284),
                Size = new Size(124, 34)
            };

            var later = new Button
            {
                Text = "Download Later",
                Location = new Point(326, 284),
                Size = new Size(120, 34)
            };

            var cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(452, 284),
                Size = new Size(92, 34),
                DialogResult = DialogResult.Cancel
            };

            start.Click += (s, e) =>
            {
                if (Check())
                    DialogResult = DialogResult.OK;
            };

            later.Click += (s, e) =>
            {
                if (Check())
                    DialogResult = DialogResult.Yes;
            };

            Controls.AddRange(new Control[]
            {
                start,
                later,
                cancel
            });

            AcceptButton = start;
            CancelButton = cancel;
        }

        // A text file with one link per line (folders and videos can be mixed).
        void LoadFromFile()
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Choose a text file with links",
                Filter = "Text files (*.txt;*.csv;*.m3u;*.m3u8)|*.txt;*.csv;*.m3u;*.m3u8|All files (*.*)|*.*"
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                if (new FileInfo(ofd.FileName).Length > 5 * 1024 * 1024)
                {
                    MessageBox.Show(this, "This file is too big for a list of links (limit 5 MB).");
                    return;
                }

                txtUrls.Text = File.ReadAllText(ofd.FileName);
                txtUrls.SelectionStart = 0;
                txtUrls.SelectionLength = 0;
                FromFile = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message);
            }
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // AddUrlForm
            // 
            ClientSize = new Size(927, 423);
            Name = "AddUrlForm";
            ResumeLayout(false);

        }

        bool Check()
        {
            if (Urls.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "Please enter at least one URL.");

                return false;
            }

            foreach (var u in FromFile ? Array.Empty<string>() : Urls)
            {
                if (!Uri.TryCreate(
                        u,
                        UriKind.Absolute,
                        out var uri) ||
                    !(uri.Scheme == Uri.UriSchemeHttp ||
                      uri.Scheme == Uri.UriSchemeHttps ||
                      uri.Scheme == Uri.UriSchemeFtp ||
                      uri.Scheme == "sftp"))
                {
                    MessageBox.Show(
                        this,
                        "Invalid link (http, https, ftp, sftp only):\n" + u);

                    return false;
                }
            }

            if (Folder.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "Please choose a folder.");

                return false;
            }

            return true;
        }
    }
}
