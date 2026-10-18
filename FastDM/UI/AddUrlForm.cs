using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FastDM
{
    class AddUrlForm : Form
    {
        readonly TextBox txtUrls;
        readonly TextBox txtName;
        readonly TextBox txtFolder;
        readonly ValidationInputFrame urlFrame;
        readonly ValidationInputFrame folderFrame;
        readonly Label lblCount;
        readonly Label lblUrlValidation;
        readonly Label lblFolderValidation;
        bool validationAttempted;
        bool loadingFile;

        public string[] Urls => txtUrls.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        public string FileNameText => txtName.Text.Trim();
        public string Folder => txtFolder.Text.Trim();
        public string RawText => txtUrls.Text;
        public bool FromFile { get; private set; }

        public AddUrlForm(string initialUrl, string folder)
        {
            Theme.ConfigureForm(this);
            Text = "Add Download";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(580, 440);
            MinimumSize = new Size(520, 380);
            Font = Theme.CreateFont(Theme.FontBody);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 1,
                RowCount = 10
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            layout.Controls.Add(new Label
            {
                Text = "URL(s) — one link per line (file or folder)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var fileRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty
            };
            var openFile = new Button
            {
                Text = "Open text file…",
                AutoSize = true,
                AccessibleName = "Open text file with links",
                AccessibleDescription = "Load a text file containing links, one per line."
            };
            openFile.Click += (s, e) => LoadFromFile();
            fileRow.Controls.Add(openFile);
            layout.Controls.Add(fileRow, 0, 1);

            urlFrame = new ValidationInputFrame { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2) };
            txtUrls = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = initialUrl ?? "",
                PlaceholderText = "Paste one link per line, e.g. https://example.com/file.zip",
                AccessibleName = "Download links",
                AccessibleDescription = "Paste one http, https, ftp, or sftp link per line."
            };
            urlFrame.Controls.Add(txtUrls);
            layout.Controls.Add(urlFrame, 0, 2);

            var countRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            countRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            countRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            lblCount = new Label
            {
                Dock = DockStyle.Fill,
                Text = "0 links detected",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.P.SubText,
                AccessibleName = "Detected link count"
            };
            lblUrlValidation = new ValidationMessageLabel
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleRight,
                AccessibleName = "Link validation message"
            };
            countRow.Controls.Add(lblCount, 0, 0);
            countRow.Controls.Add(lblUrlValidation, 1, 0);
            layout.Controls.Add(countRow, 0, 3);

            layout.Controls.Add(new Label
            {
                Text = "File name (optional, single link only)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 4);
            txtName = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = "File name",
                AccessibleDescription = "Optional output file name for a single link."
            };
            layout.Controls.Add(txtName, 0, 5);

            layout.Controls.Add(new Label { Text = "Save to", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
            folderFrame = new ValidationInputFrame { Dock = DockStyle.Fill, Margin = Padding.Empty };
            txtFolder = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Text = folder,
                AccessibleName = "Save folder",
                AccessibleDescription = "Folder where downloads will be saved."
            };
            folderFrame.Controls.Add(txtFolder);

            var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            folderRow.Controls.Add(folderFrame, 0, 0);
            var browse = new Button
            {
                Text = "Browse…",
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 0, 0),
                AccessibleName = "Browse for save folder"
            };
            browse.Click += (s, e) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK)
                    txtFolder.Text = fb.SelectedPath;
            };
            folderRow.Controls.Add(browse, 1, 0);
            layout.Controls.Add(folderRow, 0, 7);

            lblFolderValidation = new ValidationMessageLabel
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                AccessibleName = "Save folder validation message"
            };
            layout.Controls.Add(lblFolderValidation, 0, 8);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty
            };
            var cancel = new Button { Text = "Cancel", Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
            var later = new Button { Text = "Download Later", Size = new Size(120, 34) };
            var start = new Button { Text = "Start Download", Size = new Size(124, 34) };
            start.Click += (s, e) =>
            {
                if (Check()) DialogResult = DialogResult.OK;
            };
            later.Click += (s, e) =>
            {
                if (Check()) DialogResult = DialogResult.Yes;
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(later);
            buttons.Controls.Add(start);
            layout.Controls.Add(buttons, 0, 9);

            Controls.Add(layout);
            txtUrls.TextChanged += (s, e) =>
            {
                if (!loadingFile) FromFile = false;
                UpdateValidation();
            };
            txtFolder.TextChanged += (s, e) => UpdateValidation();
            UpdateValidation();

            AcceptButton = start;
            CancelButton = cancel;
        }

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
                    ThemeDialog.Show(this, "This file is too big for a list of links (limit 5 MB).");
                    return;
                }

                loadingFile = true;
                txtUrls.Text = File.ReadAllText(ofd.FileName);
                txtUrls.SelectionStart = 0;
                txtUrls.SelectionLength = 0;
                FromFile = true;
                UpdateValidation();
            }
            catch (Exception ex)
            {
                ThemeDialog.ShowError(this, "FastDM", "Couldn't read the selected text file.", ex);
            }
            finally
            {
                loadingFile = false;
            }
        }

        bool Check()
        {
            validationAttempted = true;
            bool valid = UpdateValidation();
            if (!valid)
            {
                if (!string.IsNullOrWhiteSpace(lblUrlValidation.Text)) txtUrls.Focus();
                else txtFolder.Focus();
            }
            return valid;
        }

        bool UpdateValidation()
        {
            var analysis = AddUrlValidation.Analyze(txtUrls.Text);
            lblCount.Text = analysis.DetectedCount + (analysis.DetectedCount == 1 ? " link detected" : " links detected");

            string urlError = "";
            if (validationAttempted && analysis.NonEmptyLineCount == 0)
                urlError = "Enter at least one link.";
            else if (validationAttempted && !FromFile && analysis.FirstInvalidLine > 0)
            {
                urlError = "Line " + analysis.FirstInvalidLine + ": use an absolute http, https, ftp, or sftp link.";
            }

            bool folderInvalid = validationAttempted && Folder.Length == 0;
            lblUrlValidation.Text = urlError;
            lblFolderValidation.Text = folderInvalid ? "Choose a save folder." : "";
            urlFrame.SetInvalid(urlError.Length > 0);
            folderFrame.SetInvalid(folderInvalid);
            return urlError.Length == 0 && !folderInvalid;
        }

    }
}
