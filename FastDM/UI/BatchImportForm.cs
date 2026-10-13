#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FastDM
{
    // Preview of an imported list of links: tick what to download, pick the quality and the folder.
    class BatchImportForm : Form
    {
        readonly List<BatchRow> rows;
        readonly int maxBatch;
        readonly ListView list;
        readonly ComboBox cmbQuality;
        readonly CheckBox chkFolders;
        readonly TextBox txtFolder;
        readonly Label lblCount;
        readonly Button btnStart;
        readonly Button btnLater;

        public List<BatchRow> SelectedRows { get; private set; } = new List<BatchRow>();
        public BatchQuality Quality => cmbQuality.SelectedItem as BatchQuality ?? BatchQuality.Presets[0];
        public bool FoldersAutomatically => chkFolders.Checked;
        public string SaveFolder => txtFolder.Text.Trim();

        static string KindText(BatchKind k)
        {
            switch (k)
            {
                case BatchKind.Video: return "Video";
                case BatchKind.Folder: return "Folder";
                case BatchKind.File: return "File / link";
                case BatchKind.Playlist: return "Playlist / channel";
                default: return "Not a link";
            }
        }

        // Playlists and non-links cannot be downloaded from a batch, so they cannot be ticked.
        static bool CanTick(BatchRow r) => r.Kind != BatchKind.Invalid && r.Kind != BatchKind.Playlist;

        public BatchImportForm(List<BatchRow> importRows, string defaultFolder, int maxBatchUrls)
        {
            rows = importRows;
            maxBatch = Math.Max(1, maxBatchUrls);

            Text = "Import links";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(860, 620);
            Font = new Font("Segoe UI", 9.5f);

            Controls.Add(new Label
            {
                Text = "Check the list, then choose what to download",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            });

            Controls.Add(new Label
            {
                Text = BatchImport.Summarize(rows),
                Location = new Point(16, 42),
                Size = new Size(828, 36)
            });

            list = new ListView
            {
                Location = new Point(16, 84),
                Size = new Size(828, 320),
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                MultiSelect = true
            };

            list.Columns.Add("Line", 52);
            list.Columns.Add("Type", 120);
            list.Columns.Add("Link", 420);
            list.Columns.Add("Note", 220);

            foreach (var r in rows)
            {
                var lvi = new ListViewItem(new[]
                {
                    r.Line.ToString(),
                    KindText(r.Kind),
                    BatchImport.DisplayUrl(r.Text),
                    r.Note
                })
                {
                    Tag = r,
                    Checked = r.Selected && CanTick(r)
                };

                if (!CanTick(r))
                    lvi.ForeColor = Theme.P.SubText;

                list.Items.Add(lvi);
            }

            list.ItemCheck += (s, e) =>
            {
                if (e.Index >= 0 && e.Index < rows.Count &&
                    e.NewValue == CheckState.Checked && !CanTick(rows[e.Index]))
                {
                    e.NewValue = e.CurrentValue;
                }
            };

            list.ItemChecked += (s, e) => UpdateCount();
            Controls.Add(list);

            var all = new Button { Text = "Select all", Location = new Point(16, 412), Size = new Size(96, 28) };
            var none = new Button { Text = "Select none", Location = new Point(118, 412), Size = new Size(100, 28) };
            var videos = new Button { Text = "Videos only", Location = new Point(224, 412), Size = new Size(100, 28) };

            all.Click += (s, e) => TickWhere(r => CanTick(r));
            none.Click += (s, e) => TickWhere(r => false);
            videos.Click += (s, e) => TickWhere(r => r.Kind == BatchKind.Video && !r.Duplicate && !r.AlreadyListed);

            Controls.Add(all);
            Controls.Add(none);
            Controls.Add(videos);

            lblCount = new Label { Location = new Point(336, 418), Size = new Size(508, 22) };
            Controls.Add(lblCount);

            Controls.Add(new Label { Text = "Video quality", Location = new Point(16, 460), AutoSize = true });

            cmbQuality = new ComboBox
            {
                Location = new Point(120, 456),
                Width = 260,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbQuality.Items.AddRange(BatchQuality.Presets);
            cmbQuality.SelectedIndex = 0;
            Controls.Add(cmbQuality);

            chkFolders = new CheckBox
            {
                Text = "Download whole folders automatically (no window for each folder)",
                Location = new Point(16, 492),
                AutoSize = true,
                Checked = true
            };
            Controls.Add(chkFolders);

            Controls.Add(new Label { Text = "Save to", Location = new Point(16, 530), AutoSize = true });

            txtFolder = new TextBox { Location = new Point(80, 526), Size = new Size(650, 26), Text = defaultFolder };
            Controls.Add(txtFolder);

            var browse = new Button { Text = "Browse...", Location = new Point(740, 525), Size = new Size(104, 28) };
            browse.Click += (s, e) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK)
                    txtFolder.Text = fb.SelectedPath;
            };
            Controls.Add(browse);

            btnStart = new Button { Text = "Start downloading", Location = new Point(412, 574), Size = new Size(170, 34) };
            btnLater = new Button { Text = "Download Later", Location = new Point(588, 574), Size = new Size(140, 34) };
            var cancel = new Button { Text = "Cancel", Location = new Point(734, 574), Size = new Size(110, 34), DialogResult = DialogResult.Cancel };

            btnStart.Click += (s, e) => Finish(DialogResult.OK);
            btnLater.Click += (s, e) => Finish(DialogResult.Yes);

            Controls.Add(btnStart);
            Controls.Add(btnLater);
            Controls.Add(cancel);

            AcceptButton = btnStart;
            CancelButton = cancel;

            UpdateCount();
        }

        void TickWhere(Func<BatchRow, bool> pick)
        {
            list.BeginUpdate();

            try
            {
                foreach (ListViewItem l in list.Items)
                {
                    if (l.Tag is BatchRow r)
                        l.Checked = pick(r) && CanTick(r);
                }
            }
            finally
            {
                list.EndUpdate();
            }

            UpdateCount();
        }

        void UpdateCount()
        {
            int n = list.CheckedItems.Count;
            string text = n + " selected";

            if (n > maxBatch)
                text += "  -  only the first " + maxBatch + " will be added (Preferences > Downloads > Maximum urls count)";

            lblCount.Text = text;
            btnStart.Enabled = n > 0;
            btnLater.Enabled = n > 0;
        }

        void Finish(DialogResult result)
        {
            if (txtFolder.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "Please choose a folder.");
                return;
            }

            SelectedRows = list.Items
                .Cast<ListViewItem>()
                .Where(l => l.Checked && l.Tag is BatchRow)
                .Select(l => (BatchRow)l.Tag!)
                .ToList();

            if (SelectedRows.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one link.");
                return;
            }

            DialogResult = result;
        }
    }
}
