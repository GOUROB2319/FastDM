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
            Theme.ConfigureForm(this);
            rows = importRows;
            maxBatch = Math.Max(1, maxBatchUrls);

            Text = "Import links";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(860, 620);
            MinimumSize = new Size(700, 500);
            Font = Theme.CreateFont(Theme.FontBody);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Theme.Scale(this, DialogLayout.StandardMargin)),
                Margin = Padding.Empty,
                ColumnCount = 1,
                RowCount = 7
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            body.Controls.Add(new Label
            {
                Text = "Check the list, then choose what to download",
                Font = Theme.CreateFont(Theme.FontHeading, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Theme.Scale(this, 8))
            }, 0, 0);

            var summary = new Label
            {
                Text = BatchImport.Summarize(rows),
                AutoSize = true,
                MaximumSize = new Size(Theme.Scale(this, 1200), 0),
                Margin = new Padding(0, 0, 0, Theme.Scale(this, 10)),
                AccessibleName = "Import summary"
            };
            body.Controls.Add(summary, 0, 1);

            list = new ListView
            {
                Dock = DockStyle.Fill,
                MinimumSize = new Size(Theme.Scale(this, 320), Theme.Scale(this, 160)),
                Margin = new Padding(0, 0, 0, Theme.Scale(this, 8)),
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                MultiSelect = true,
                AccessibleName = "Import links",
                AccessibleDescription = "Select the links to import. Unsupported playlist and non-link rows cannot be selected."
            };

            list.Columns.Add("Line", 52);
            list.Columns.Add("Type", 120);
            list.Columns.Add("Link", 420);
            list.Columns.Add("Note", 220);
            list.ClientSizeChanged += (s, e) => FitBatchColumns();

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
            body.Controls.Add(list, 0, 2);

            var selectionRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Theme.Scale(this, 8)),
                ColumnCount = 4,
                RowCount = 1
            };
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 104)));
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 108)));
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 112)));
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var all = DialogLayout.CreateButton(this, "Select all");
            var none = DialogLayout.CreateButton(this, "Select none");
            var videos = DialogLayout.CreateButton(this, "Videos only");
            all.Margin = new Padding(0, Theme.Scale(this, 2), Theme.Scale(this, 6), Theme.Scale(this, 2));
            none.Margin = new Padding(0, Theme.Scale(this, 2), Theme.Scale(this, 6), Theme.Scale(this, 2));
            videos.Margin = new Padding(0, Theme.Scale(this, 2), Theme.Scale(this, 6), Theme.Scale(this, 2));
            all.Click += (s, e) => TickWhere(r => CanTick(r));
            none.Click += (s, e) => TickWhere(r => false);
            videos.Click += (s, e) => TickWhere(r => r.Kind == BatchKind.Video && !r.Duplicate && !r.AlreadyListed);

            lblCount = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0),
                AccessibleName = "Selected link count"
            };
            selectionRow.Controls.Add(all, 0, 0);
            selectionRow.Controls.Add(none, 1, 0);
            selectionRow.Controls.Add(videos, 2, 0);
            selectionRow.Controls.Add(lblCount, 3, 0);
            body.Controls.Add(selectionRow, 0, 3);

            var qualityRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Theme.Scale(this, 8)),
                ColumnCount = 3,
                RowCount = 1
            };
            qualityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 112)));
            qualityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 260)));
            qualityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            qualityRow.Controls.Add(new Label
            {
                Text = "Video quality",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            cmbQuality = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "Video quality"
            };
            cmbQuality.Items.AddRange(BatchQuality.Presets);
            cmbQuality.SelectedIndex = 0;
            qualityRow.Controls.Add(cmbQuality, 1, 0);
            body.Controls.Add(qualityRow, 0, 4);

            chkFolders = new CheckBox
            {
                Text = "Download whole folders automatically (no window for each folder)",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Theme.Scale(this, 8)),
                Checked = true,
                AccessibleName = "Download whole folders automatically"
            };
            body.Controls.Add(chkFolders, 0, 5);

            var folderRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = Padding.Empty,
                ColumnCount = 3,
                RowCount = 1
            };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 64)));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 112)));
            folderRow.Controls.Add(new Label
            {
                Text = "Save to",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            txtFolder = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = defaultFolder,
                AccessibleName = "Save folder"
            };
            folderRow.Controls.Add(txtFolder, 1, 0);

            var browse = DialogLayout.CreateButton(this, "Browse…");
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(Theme.Scale(this, 6), 0, 0, 0);
            browse.Click += (s, e) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK)
                    txtFolder.Text = fb.SelectedPath;
            };
            folderRow.Controls.Add(browse, 2, 0);
            body.Controls.Add(folderRow, 0, 6);

            btnStart = DialogLayout.CreateButton(this, "Start download");
            btnLater = DialogLayout.CreateButton(this, "Download Later");
            var cancel = DialogLayout.CreateButton(this, "Cancel", DialogResult.Cancel);
            btnStart.Click += (s, e) => Finish(DialogResult.OK);
            btnLater.Click += (s, e) => Finish(DialogResult.Yes);

            var footer = DialogLayout.CreateFooter(this, cancel, btnLater, btnStart);
            Controls.Add(DialogLayout.CreateShell(this, body, footer));

            AcceptButton = btnStart;
            CancelButton = cancel;
            Resize += (s, e) =>
            {
                summary.MaximumSize = new Size(Math.Max(Theme.Scale(this, 240), body.ClientSize.Width), 0);
                FitBatchColumns();
            };

            UpdateCount();
            FitBatchColumns();
            Shown += (s, e) => list.Focus();
        }

        void FitBatchColumns()
        {
            if (list.Columns.Count < 4 || list.ClientSize.Width <= 0) return;
            int available = Math.Max(0, list.ClientSize.Width - 4);
            const int line = 52, type = 120, note = 160, minimumLink = 160;
            int link = Math.Max(minimumLink, available - line - type - note);
            int[] widths = { line, type, link, Math.Max(note, available - line - type - link) };
            for (int i = 0; i < widths.Length; i++)
                if (list.Columns[i].Width != widths[i])
                    list.Columns[i].Width = widths[i];
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
                ThemeDialog.Show(this, "Please choose a folder.");
                return;
            }

            SelectedRows = list.Items
                .Cast<ListViewItem>()
                .Where(l => l.Checked && l.Tag is BatchRow)
                .Select(l => (BatchRow)l.Tag!)
                .ToList();

            if (SelectedRows.Count == 0)
            {
                ThemeDialog.Show(this, "Tick at least one link.");
                return;
            }

            DialogResult = result;
        }
    }
}
