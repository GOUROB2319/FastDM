using System;
using System.Drawing;
using System.Windows.Forms;

namespace FastDM
{
    sealed class YtDlpErrorForm : Form
    {
        readonly TextBox details;
        readonly CheckBox showDetails;
        public bool OpenVideoSettings { get; private set; }
        public bool RetryWithoutCookies { get; private set; }

        public YtDlpErrorForm(string error, YtDlpCookieSource source, bool packaged, bool canRetryWithoutCookies = true)
        {
            Theme.ConfigureForm(this);
            var kind = YtDlpDiagnostics.Classify(error, source);
            var description = YtDlpDiagnostics.Describe(kind, packaged);
            bool offerNoCookieRetry = canRetryWithoutCookies && YtDlpDiagnostics.CanRetryWithoutCookies(kind);
            string safeError = AppLog.Redact(error);

            Text = "Video couldn't be accessed";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.CreateFont(Theme.FontBody);
            ClientSize = new Size(700, 390);
            MinimumSize = new Size(560, 340);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                ColumnCount = 1,
                RowCount = 5
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(new Label
            {
                Text = description.Summary,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10),
                AccessibleName = "Video error summary"
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = "Possible cause / next step: " + description.NextStep,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Margin = new Padding(0, 0, 0, 12),
                AccessibleName = "Suggested next step"
            }, 0, 1);

            showDetails = new CheckBox
            {
                Text = "Show technical details",
                AutoSize = true,
                AccessibleName = "Show technical details",
                Margin = new Padding(0, 0, 0, 6)
            };
            layout.Controls.Add(showDetails, 0, 2);

            details = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
                Visible = false,
                Text = safeError,
                AccessibleName = "Redacted yt-dlp error details",
                Margin = new Padding(0, 0, 0, 10)
            };
            layout.Controls.Add(details, 0, 3);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            var close = new Button { Text = "Close", Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
            var open = new Button { Text = "Open video settings", Size = new Size(150, 34) };
            var copy = new Button { Text = "Copy diagnostic details", Size = new Size(170, 34) };
            Button? retry = offerNoCookieRetry ? new Button { Text = "Retry without cookies", Size = new Size(160, 34) } : null;
            close.Click += (s, e) => Close();
            open.Click += (s, e) =>
            {
                OpenVideoSettings = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(AppLog.Redact(details.Text));
                    ThemeDialog.Show(this, "Redacted diagnostic details copied.", "FastDM");
                }
                catch (Exception ex) { ThemeDialog.ShowError(this, "FastDM", "Couldn't copy diagnostic details. Try again.", ex); }
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(open);
            buttons.Controls.Add(copy);
            if (retry != null)
            {
                retry.Click += (s, e) =>
                {
                    RetryWithoutCookies = true;
                    DialogResult = DialogResult.Retry;
                    Close();
                };
                buttons.Controls.Add(retry);
            }
            layout.Controls.Add(buttons, 0, 4);

            showDetails.CheckedChanged += (s, e) =>
            {
                details.Visible = showDetails.Checked;
                layout.RowStyles[3].SizeType = showDetails.Checked ? SizeType.Percent : SizeType.Absolute;
                layout.RowStyles[3].Height = showDetails.Checked ? 100 : 0;
                if (showDetails.Checked && Height < 470) Height = 470;
            };

            Controls.Add(layout);
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
