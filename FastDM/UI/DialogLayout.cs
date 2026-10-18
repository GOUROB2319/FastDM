using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FastDM
{
    // Shared layout primitives for programmatic dialogs. Dimensions are expressed in 96-DPI logical pixels.
    internal static class DialogLayout
    {
        public const int StandardMargin = 16;
        public const int StandardButtonHeight = 34;
        public const int StandardButtonMinWidth = 96;

        public static TableLayoutPanel CreateTwoColumnFields(Form owner, int labelWidth = 116)
        {
            int margin = Theme.Scale(owner, StandardMargin);
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                AutoSize = false,
                Padding = new Padding(margin),
                Margin = Padding.Empty,
                ColumnCount = 2,
                RowCount = 0,
                BackColor = Theme.P.Window
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(owner, labelWidth)));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        public static void AddFieldRow(TableLayoutPanel table, string labelText, Control field)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, Theme.Scale(table, 4), Theme.Scale(table, 10), Theme.Scale(table, 4))
            };

            field.Dock = field is CheckBox or RadioButton ? DockStyle.Left : DockStyle.Fill;
            field.Margin = new Padding(0, Theme.Scale(table, 3), 0, Theme.Scale(table, 3));
            table.Controls.Add(label, 0, row);
            table.Controls.Add(field, 1, row);
        }

        public static void AddFullRow(TableLayoutPanel table, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, Theme.Scale(table, 4), 0, Theme.Scale(table, 4));
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, 2);
        }

        public static Button CreateButton(Form owner, string text, DialogResult result = DialogResult.None)
        {
            int height = Theme.Scale(owner, StandardButtonHeight);
            int minWidth = Theme.Scale(owner, StandardButtonMinWidth);
            int textWidth = TextRenderer.MeasureText(text, owner.Font).Width + Theme.Scale(owner, 28);
            return new Button
            {
                Text = text,
                AutoSize = false,
                Size = new Size(Math.Max(minWidth, textWidth), height),
                MinimumSize = new Size(minWidth, height),
                DialogResult = result,
                Margin = new Padding(Theme.Scale(owner, 6), 0, 0, 0)
            };
        }

        // Buttons are passed in visual order from right to left; the first is the rightmost button.
        public static FlowLayoutPanel CreateFooter(Form owner, params Button[] buttons)
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = Padding.Empty,
                Padding = new Padding(Theme.Scale(owner, StandardMargin), Theme.Scale(owner, 10),
                    Theme.Scale(owner, StandardMargin), Theme.Scale(owner, 8)),
                BackColor = Theme.P.Window
            };
            int sharedMinWidth = Theme.Scale(owner, StandardButtonMinWidth);
            int sharedWidth = buttons
                .Select(button => Math.Max(sharedMinWidth, TextRenderer.MeasureText(button.Text, owner.Font).Width + Theme.Scale(owner, 28)))
                .DefaultIfEmpty(sharedMinWidth)
                .Max();

            foreach (var button in buttons)
            {
                int height = Theme.Scale(owner, StandardButtonHeight);
                button.MinimumSize = new Size(sharedWidth, height);
                button.Size = new Size(sharedWidth, height);
                button.Margin = new Padding(Theme.Scale(owner, 6), 0, 0, 0);
                footer.Controls.Add(button);
            }
            footer.SizeChanged += (s, e) =>
            {
                // Keep the footer compact; button overflow is avoided by resizing/wrapping the dialog content.
                foreach (Control control in footer.Controls)
                    if (control.Height != Theme.Scale(owner, StandardButtonHeight))
                        control.Height = Theme.Scale(owner, StandardButtonHeight);
            };
            return footer;
        }

        public static TableLayoutPanel CreateShell(Form owner, Control content, FlowLayoutPanel footer)
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.P.Window
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(owner, 58)));
            content.Dock = DockStyle.Fill;
            footer.Dock = DockStyle.Fill;
            shell.Controls.Add(content, 0, 0);
            shell.Controls.Add(footer, 0, 1);
            return shell;
        }

        public static void TrackDirty(Control root, Action markDirty)
        {
            EventHandler changed = (_, _) => markDirty();
            TrackDirtyCore(root, changed);
        }

        static void TrackDirtyCore(Control root, EventHandler changed)
        {
            foreach (Control control in root.Controls)
            {
                switch (control)
                {
                    case TextBoxBase text:
                        text.TextChanged += changed;
                        break;
                    case CheckBox check:
                        check.CheckedChanged += changed;
                        break;
                    case RadioButton radio:
                        radio.CheckedChanged += changed;
                        break;
                    case ComboBox combo:
                        combo.SelectedIndexChanged += changed;
                        break;
                    case NumericUpDown number:
                        number.ValueChanged += changed;
                        break;
                    case DateTimePicker date:
                        date.ValueChanged += changed;
                        break;
                    case TrackBar track:
                        track.ValueChanged += changed;
                        break;
                }
                if (control.HasChildren) TrackDirtyCore(control, changed);
            }
        }

        public static void GuardUnsavedChanges(Form owner, bool dirty, FormClosingEventArgs e)
        {
            if (!dirty || owner.DialogResult == DialogResult.OK || owner.DialogResult == DialogResult.Yes)
                return;

            if (ThemeDialog.Confirm(owner, "Discard changes?", "Discard", "Keep editing") == DialogResult.Yes)
            {
                owner.DialogResult = DialogResult.Cancel;
                return;
            }

            e.Cancel = true;
            owner.DialogResult = DialogResult.None;
        }

        public static void FocusFirstEditable(Control root) => TryFocusFirstEditable(root);

        static bool TryFocusFirstEditable(Control root)
        {
            foreach (Control control in root.Controls)
            {
                if (!control.Visible || !control.Enabled) continue;
                if (control is TextBoxBase or ComboBox or NumericUpDown)
                {
                    if (control.CanFocus && control.Focus()) return true;
                }
                if (control.HasChildren && TryFocusFirstEditable(control)) return true;
            }
            return false;
        }
    }
}
