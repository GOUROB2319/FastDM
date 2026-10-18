using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FastDM
{
    // A small WinForms dialog that follows the active FastDM palette.
    public static class ThemeDialog
    {
        public static DialogResult Show(IWin32Window owner, string message) =>
            Show(owner, message, "FastDM", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);

        public static DialogResult Show(IWin32Window owner, string message, string caption) =>
            Show(owner, message, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);

        public static DialogResult Show(IWin32Window owner, string message, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon) =>
            Show(owner, message, caption, buttons, icon, MessageBoxDefaultButton.Button1);

        public static DialogResult Show(IWin32Window owner, string message, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            var palette = Theme.P;
            string safeMessage = AppLog.Redact(message ?? string.Empty);
            var workArea = owner is Control control
                ? Screen.FromControl(control).WorkingArea
                : Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
            int maxWidth = Math.Max(300, (int)(workArea.Width * 0.9) - 32);
            int maxHeight = Math.Max(220, (int)(workArea.Height * 0.9) - 48);
            int width = Math.Min(720, maxWidth);
            int textWidth = Math.Max(160, width - 90);
            using var measureFont = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.5f);
            var measured = TextRenderer.MeasureText(safeMessage, measureFont,
                new Size(textWidth, maxHeight), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            int bodyHeight = Math.Clamp(measured.Height + 8, 58, Math.Max(58, maxHeight - 104));

            var dialog = new Form
            {
                Text = string.IsNullOrWhiteSpace(caption) ? "FastDM" : AppLog.Redact(caption),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MinimizeBox = false,
                MaximizeBox = false,
                Font = SystemFonts.MessageBoxFont,
                ClientSize = new Size(width, bodyHeight + 104)
            };
            Theme.ConfigureForm(dialog);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 2,
                RowCount = 2
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(dialog, 38)));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(dialog, 48)));

            var symbol = new Label
            {
                Dock = DockStyle.Fill,
                Text = IconText(icon),
                TextAlign = ContentAlignment.TopCenter,
                Font = dialog.Font,
                ForeColor = IconColor(icon, palette),
                BackColor = palette.Window
            };
            root.Controls.Add(symbol, 0, 0);

            var body = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = palette.Window,
                ForeColor = palette.Text,
                Text = safeMessage,
                AccessibleName = "Message"
            };
            root.Controls.Add(body, 1, 0);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = new Padding(0, 8, 0, 0)
            };
            var choices = Choices(buttons);
            Button? accept = null;
            Button? cancel = null;
            int defaultIndex = defaultButton switch
            {
                MessageBoxDefaultButton.Button2 => 1,
                MessageBoxDefaultButton.Button3 => 2,
                _ => 0
            };
            Button? selectedDefault = null;

            foreach (var choice in choices.AsEnumerable().Reverse())
            {
                var button = new Button
                {
                    Text = choice.Text,
                    Size = new Size(Theme.Scale(dialog, 92), Theme.Scale(dialog, 32)),
                    Margin = new Padding(Theme.Scale(dialog, 6), 0, 0, 0),
                    DialogResult = choice.Result
                };
                Theme.StyleDialogButton(button, isPrimary: choice.Result == choices[0].Result);
                actions.Controls.Add(button);
                if (choice.Result == choices[0].Result) accept = button;
                if (choice.Result == DialogResult.Cancel || (buttons == MessageBoxButtons.YesNo && choice.Result == DialogResult.No)) cancel = button;
                if (choices.IndexOf(choice) == defaultIndex) selectedDefault = button;
            }
            root.Controls.Add(actions, 0, 1);
            root.SetColumnSpan(actions, 2);
            dialog.Controls.Add(root);
            dialog.AcceptButton = selectedDefault ?? accept;
            dialog.CancelButton = cancel;
            Theme.Apply(dialog);
            body.BorderStyle = BorderStyle.None;
            body.BackColor = palette.Window;
            return dialog.ShowDialog(owner);
        }


        public static DialogResult Confirm(IWin32Window owner, string message,
            string discardText = "Discard", string keepEditingText = "Keep editing")
        {
            var palette = Theme.P;
            var dialog = new Form
            {
                Text = "FastDM",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(460, 172),
                Font = Theme.CreateFont(Theme.FontBody)
            };
            Theme.ConfigureForm(dialog);

            var body = new Label
            {
                Text = AppLog.Redact(message ?? string.Empty),
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
                ForeColor = palette.Text
            };

            var keep = new Button
            {
                Text = keepEditingText,
                Size = new Size(Theme.Scale(dialog, 116), Theme.Scale(dialog, 34)),
                DialogResult = DialogResult.No
            };
            var discard = new Button
            {
                Text = discardText,
                Size = new Size(Theme.Scale(dialog, 104), Theme.Scale(dialog, 34)),
                DialogResult = DialogResult.Yes
            };
            var footer = DialogLayout.CreateFooter(dialog, keep, discard);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(Theme.Scale(dialog, 20))
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(dialog, 58)));
            layout.Controls.Add(body, 0, 0);
            layout.Controls.Add(footer, 0, 1);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = keep;
            dialog.CancelButton = keep;
            Theme.Apply(dialog);
            Theme.StyleDialogButton(keep, isPrimary: false);
            Theme.StyleDialogButton(discard, isPrimary: true);
            dialog.Shown += (s, e) => keep.Focus();
            return dialog.ShowDialog(owner);
        }

        public static DialogResult ShowError(IWin32Window owner, string caption, string userMessage, Exception exception)
        {
            string detail = AppLog.Redact(exception.GetBaseException().Message);
            AppLog.Write(caption + " (error detail): " + detail);
            return Show(owner, userMessage, caption, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
        }

        static List<(string Text, DialogResult Result)> Choices(MessageBoxButtons buttons) => buttons switch
        {
            MessageBoxButtons.OKCancel => new List<(string, DialogResult)> { ("OK", DialogResult.OK), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.YesNo => new List<(string, DialogResult)> { ("Yes", DialogResult.Yes), ("No", DialogResult.No) },
            MessageBoxButtons.YesNoCancel => new List<(string, DialogResult)> { ("Yes", DialogResult.Yes), ("No", DialogResult.No), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.RetryCancel => new List<(string, DialogResult)> { ("Retry", DialogResult.Retry), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.AbortRetryIgnore => new List<(string, DialogResult)> { ("Abort", DialogResult.Abort), ("Retry", DialogResult.Retry), ("Ignore", DialogResult.Ignore) },
            _ => new List<(string, DialogResult)> { ("OK", DialogResult.OK) }
        };

        static string IconText(MessageBoxIcon icon) => icon switch
        {
            MessageBoxIcon.Error => "×",
            MessageBoxIcon.Warning => "!",
            MessageBoxIcon.Question => "?",
            MessageBoxIcon.Information => "i",
            _ => string.Empty
        };

        static Color IconColor(MessageBoxIcon icon, Palette palette) => icon switch
        {
            MessageBoxIcon.Error => palette.Error,
            MessageBoxIcon.Warning => palette.Warning,
            MessageBoxIcon.Information => palette.Success,
            MessageBoxIcon.Question => palette.AccentText,
            _ => palette.Text
        };
    }
}
