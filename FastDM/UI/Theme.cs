using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastDM
{
    public enum ThemeMode { System, Light, Dark }

    public class Palette
    {
        public Color Window, RowAlt, Text, SubText, Border, InputBorder, Accent, AccentText, Selection, Track,
                     Header, Toolbar, Hover, Pressed, Input, Button, Success, Warning, Error,
                     SideBg, SideSel, SideText, SideCount;
    }

    // ====================== Theme Manager ======================
    public static class Theme
    {
        static readonly SizeF LogicalDpi = new SizeF(96f, 96f);
        public const float FontSmall = 8.5f;
        public const float FontBody = 9.5f;
        public const float FontSection = 10f;
        public const float FontHeading = 11f;
        public const float FontTitle = 16f;

        public static Font CreateFont(float points, FontStyle style = FontStyle.Regular) =>
            new Font("Segoe UI", points, style, GraphicsUnit.Point);

        // Programmatic forms use the same 96 DPI design baseline as designer forms.
        // This keeps control bounds and fonts in one coordinate system as Windows DPI changes.
        public static void ConfigureForm(Form form, bool clampToWorkArea = true)
        {
            if (form == null) return;
            form.AutoScaleDimensions = LogicalDpi;
            form.AutoScaleMode = AutoScaleMode.Dpi;
            if (clampToWorkArea)
            {
                form.Shown -= ClampInitialDialog;
                form.Shown += ClampInitialDialog;
            }
        }

        public static int Scale(Control control, int logicalPixels)
        {
            int dpi = control?.DeviceDpi ?? 96;
            return Math.Max(1, (int)Math.Round(logicalPixels * dpi / 96.0));
        }

        static void ClampInitialDialog(object? sender, EventArgs e)
        {
            if (sender is not Form form || form.IsDisposed) return;
            Rectangle area = Screen.FromControl(form).WorkingArea;
            int maxWidth = Math.Max(320, (int)(area.Width * 0.9));
            int maxHeight = Math.Max(240, (int)(area.Height * 0.9));
            int width = Math.Min(form.Width, maxWidth);
            int height = Math.Min(form.Height, maxHeight);
            if (width < form.Width || height < form.Height)
            {
                form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, width), Math.Min(form.MinimumSize.Height, height));
                form.Size = new Size(width, height);
            }
        }

        public static readonly Palette Light = new Palette
        {
            Window = ColorTranslator.FromHtml("#FFFFFF"),
            RowAlt = ColorTranslator.FromHtml("#F8F9FC"),
            Text = ColorTranslator.FromHtml("#2D3142"),
            SubText = ColorTranslator.FromHtml("#555B70"),
            Border = ColorTranslator.FromHtml("#E3E6EF"),
            InputBorder = ColorTranslator.FromHtml("#717B96"),
            Accent = ColorTranslator.FromHtml("#5B6CFF"),
            AccentText = ColorTranslator.FromHtml("#424CC4"),
            Selection = ColorTranslator.FromHtml("#DEE6FF"),
            Track = ColorTranslator.FromHtml("#E6E9F2"),
            Header = ColorTranslator.FromHtml("#F4F5FA"),
            Toolbar = ColorTranslator.FromHtml("#FFFFFF"),
            Hover = ColorTranslator.FromHtml("#EDEFF7"),
            Pressed = ColorTranslator.FromHtml("#DCE0F0"),
            Input = ColorTranslator.FromHtml("#FFFFFF"),
            Button = ColorTranslator.FromHtml("#F4F5FA"),
            Success = ColorTranslator.FromHtml("#115F31"),
            Warning = ColorTranslator.FromHtml("#875700"),
            Error = ColorTranslator.FromHtml("#B42338"),
            SideBg = ColorTranslator.FromHtml("#1E2233"),
            SideSel = ColorTranslator.FromHtml("#2F3550"),
            SideText = ColorTranslator.FromHtml("#FFFFFF"),
            SideCount = ColorTranslator.FromHtml("#A0A8C8")
        };

        public static readonly Palette Dark = new Palette
        {
            Window = ColorTranslator.FromHtml("#171923"),
            RowAlt = ColorTranslator.FromHtml("#1C1F2B"),
            Text = ColorTranslator.FromHtml("#E6E8F2"),
            SubText = ColorTranslator.FromHtml("#B6BED2"),
            Border = ColorTranslator.FromHtml("#2C3043"),
            InputBorder = ColorTranslator.FromHtml("#99A5C2"),
            Accent = ColorTranslator.FromHtml("#7482FF"),
            AccentText = ColorTranslator.FromHtml("#BEC7FF"),
            Selection = ColorTranslator.FromHtml("#2A3157"),
            Track = ColorTranslator.FromHtml("#2C3043"),
            Header = ColorTranslator.FromHtml("#1F2230"),
            Toolbar = ColorTranslator.FromHtml("#1F2230"),
            Hover = ColorTranslator.FromHtml("#2A2E42"),
            Pressed = ColorTranslator.FromHtml("#343958"),
            Input = ColorTranslator.FromHtml("#1F2230"),
            Button = ColorTranslator.FromHtml("#262A3B"),
            Success = ColorTranslator.FromHtml("#6CD998"),
            Warning = ColorTranslator.FromHtml("#FFD166"),
            Error = ColorTranslator.FromHtml("#FF91A0"),
            SideBg = ColorTranslator.FromHtml("#11131B"),
            SideSel = ColorTranslator.FromHtml("#23283D"),
            SideText = ColorTranslator.FromHtml("#FFFFFF"),
            SideCount = ColorTranslator.FromHtml("#8C93B3")
        };

        public static ThemeMode Mode { get; private set; } = ThemeMode.System;
        public static Palette P { get; private set; } = Light;
        public static bool IsDark { get; private set; }

        static readonly ConditionalWeakTable<Control, InputBorderWindow> InputBorders = new();

        static double Luminance(Color color)
        {
            static double Linear(byte value)
            {
                double c = value / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        public static double ContrastRatio(Color foreground, Color background)
        {
            double a = Luminance(foreground);
            double b = Luminance(background);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        public static Color ContrastText(Color background) =>
            ContrastRatio(Color.White, background) >= ContrastRatio(Color.Black, background)
                ? Color.White
                : Color.Black;

        public static void SetMode(ThemeMode mode)
        {
            Mode = mode;
            IsDark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemIsDark());
            P = IsDark ? Dark : Light;
        }

        // Detect whether Windows is using a dark application theme.
        public static bool SystemIsDark()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        // ---------- Dark title bar ----------
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void SetTitleBar(Form f)
        {
            void apply()
            {
                try
                {
                    int v = IsDark ? 1 : 0;
                    DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
                }
                catch (Exception exIgnored1) { AppLog.Write("Theme (ignored): " + exIgnored1.Message); }
            }
            if (f.IsHandleCreated) apply(); else f.HandleCreated += (s, e) => apply();
        }

        // ---------- Scrollbars ----------
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

        public static void StyleScroll(Control c)
        {
            void apply()
            {
                try { SetWindowTheme(c.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); }
                catch (Exception exIgnored2) { AppLog.Write("Theme (ignored): " + exIgnored2.Message); }
            }
            if (c.IsHandleCreated) apply(); else c.HandleCreated += (s, e) => apply();
        }

        // ---------- ToolStrip / StatusStrip / ContextMenuStrip ----------
        public static void StyleStrip(ToolStrip ts)
        {
            if (ts == null) return;
            ts.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable()) { RoundedEdges = false };
            ts.BackColor = P.Toolbar;
            ts.ForeColor = P.Text;
            foreach (ToolStripItem item in ts.Items)
            {
                if (item is ToolStripTextBox text)
                {
                    text.BackColor = P.Input;
                    text.ForeColor = P.Text;
                    text.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (item is ToolStripDropDownItem dropDown)
                {
                    foreach (ToolStripItem child in dropDown.DropDownItems)
                        if (child is ToolStripTextBox childText)
                        {
                            childText.BackColor = P.Input;
                            childText.ForeColor = P.Text;
                            childText.BorderStyle = BorderStyle.FixedSingle;
                        }
                }
            }
        }

        // ---------- Apply theme to all controls in the form/dialog ----------
        public static void Apply(Form f)
        {
            if (f.AutoScaleMode != AutoScaleMode.Dpi)
                ConfigureForm(f);
            f.BackColor = P.Window;
            f.ForeColor = P.Text;
            SetTitleBar(f);
            ApplyChildren(f);
            f.PerformLayout();
            ApplyAccessibility(f);
            if (f.AcceptButton is Button primary) StyleButton(primary, primary: true);
        }

        static void ApplyAccessibility(Control parent)
        {
            var ordered = parent.Controls.Cast<Control>()
                .OrderBy(c => c.Top)
                .ThenBy(c => c.Left)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
                ordered[i].TabIndex = i;

            var fallbackCounts = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Control control in ordered)
            {
                if (IsInteractive(control))
                {
                    if (string.IsNullOrWhiteSpace(control.AccessibleName))
                    {
                        string? label = NearbyLabel(control);
                        string name = label ?? AccessibleControlKind(control);
                        if (label == null)
                        {
                            fallbackCounts.TryGetValue(name, out int count);
                            fallbackCounts[name] = ++count;
                            name += " " + count;
                        }
                        control.AccessibleName = name;
                    }

                    if (string.IsNullOrWhiteSpace(control.AccessibleDescription))
                        control.AccessibleDescription = AccessibleDescriptionFor(control, control.AccessibleName);
                }

                if (control is ToolStrip strip)
                    ApplyToolStripAccessibility(strip);
                if (control.HasChildren)
                    ApplyAccessibility(control);
            }
        }

        static bool IsInteractive(Control control) =>
            control.TabStop || control is ListView || control is ListBox || control is TreeView || control is TabControl;

        static string? NearbyLabel(Control control)
        {
            if (control.Parent == null) return null;
            return control.Parent.Controls.OfType<Label>()
                .Where(label => !ReferenceEquals(label, control) && !string.IsNullOrWhiteSpace(label.Text))
                .Select(label => new
                {
                    Label = label,
                    Score = (label.Bottom <= control.Top
                        ? control.Top - label.Bottom
                        : Math.Abs(control.Top - label.Top) + 40) + Math.Abs(control.Left - label.Left) / 4
                })
                .OrderBy(x => x.Score)
                .Select(x => x.Label)
                .FirstOrDefault()?.Text.Trim().TrimEnd(':');
        }

        static string AccessibleControlKind(Control control) => control switch
        {
            TextBoxBase => "Text input",
            ComboBox => "Option list",
            NumericUpDown => "Number input",
            CheckBox => "Checkbox",
            RadioButton => "Radio button",
            ListView => "Items list",
            TreeView => "Folder tree",
            ListBox => "Options list",
            Button => "Button",
            LinkLabel => "Link",
            TabControl => "Tabs",
            _ => control.GetType().Name
        };

        static string AccessibleDescriptionFor(Control control, string name) => control switch
        {
            TextBoxBase => "Enter " + name.ToLowerInvariant() + ".",
            ComboBox => "Choose " + name.ToLowerInvariant() + ".",
            NumericUpDown => "Adjust " + name.ToLowerInvariant() + ".",
            CheckBox => "Toggle this option.",
            RadioButton => "Select this option.",
            TreeView => "Use the arrow keys to browse folders and Space to toggle a selection.",
            ListView or ListBox => "Use the arrow keys to move through the list.",
            Button => "Activate this button.",
            LinkLabel => "Open this link.",
            _ => name
        };

        static void ApplyToolStripAccessibility(ToolStrip strip)
        {
            foreach (ToolStripItem item in strip.Items)
            {
                if (string.IsNullOrWhiteSpace(item.AccessibleName))
                    item.AccessibleName = string.IsNullOrWhiteSpace(item.Text)
                        ? item.GetType().Name
                        : item.Text.Replace("&", "").Trim();
                if (string.IsNullOrWhiteSpace(item.AccessibleDescription))
                    item.AccessibleDescription = string.IsNullOrWhiteSpace(item.ToolTipText)
                        ? item.AccessibleName
                        : item.ToolTipText;

                if (item is ToolStripDropDownItem dropDown)
                    foreach (ToolStripItem child in dropDown.DropDownItems)
                    {
                        if (string.IsNullOrWhiteSpace(child.AccessibleName))
                            child.AccessibleName = string.IsNullOrWhiteSpace(child.Text)
                                ? child.GetType().Name
                                : child.Text.Replace("&", "").Trim();
                        if (string.IsNullOrWhiteSpace(child.AccessibleDescription))
                            child.AccessibleDescription = string.IsNullOrWhiteSpace(child.ToolTipText)
                                ? child.AccessibleName
                                : child.ToolTipText;
                    }
            }
        }

        static void ApplyChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c.ContextMenuStrip != null) StyleStrip(c.ContextMenuStrip);
                switch (c)
                {
                    case Button b:
                        StyleButton(b, primary: false);
                        break;
                    case ValidationInputFrame frame:
                        frame.BackColor = P.Input;
                        break;
                    case TextBoxBase t:
                        t.BackColor = P.Input;
                        t.ForeColor = P.Text;
                        t.BorderStyle = t.Parent is ValidationInputFrame ? BorderStyle.None : BorderStyle.FixedSingle;
                        break;
                    case ComboBox cb:
                        cb.BackColor = P.Input;
                        cb.ForeColor = P.Text;
                        cb.FlatStyle = FlatStyle.Flat;
                        if (cb.DrawMode == DrawMode.Normal)
                        {
                            cb.DrawMode = DrawMode.OwnerDrawFixed;
                            cb.ItemHeight = Scale(cb, 22);
                            cb.DrawItem -= DrawComboItem;
                            cb.DrawItem += DrawComboItem;
                        }
                        StyleScroll(cb);
                        break;
                    case NumericUpDown n:
                        n.BackColor = P.Input;
                        n.ForeColor = P.Text;
                        n.BorderStyle = BorderStyle.FixedSingle;
                        StyleScroll(n);
                        break;
                    case CheckedListBox clb:
                        clb.BackColor = P.Input;
                        clb.ForeColor = P.Text;
                        clb.BorderStyle = BorderStyle.FixedSingle;
                        StyleScroll(clb);
                        if (clb.DrawMode == DrawMode.Normal)
                        {
                            clb.DrawMode = DrawMode.OwnerDrawFixed;
                            clb.ItemHeight = Scale(clb, 24);
                            clb.DrawItem -= DrawCheckedListItem;
                            clb.DrawItem += DrawCheckedListItem;
                        }
                        break;
                    case ListBox lb:
                        lb.BackColor = P.Input;
                        lb.ForeColor = P.Text;
                        lb.BorderStyle = BorderStyle.FixedSingle;
                        StyleScroll(lb);
                        if (lb.DrawMode == DrawMode.Normal)
                        {
                            lb.DrawMode = DrawMode.OwnerDrawFixed;
                            lb.ItemHeight = Scale(lb, 22);
                            lb.DrawItem -= DrawListItem;
                            lb.DrawItem += DrawListItem;
                        }
                        break;
                    case ListView lv:
                        lv.BackColor = P.Input;
                        lv.ForeColor = P.Text;
                        StyleScroll(lv);
                        break;
                    case TreeView tv:
                        tv.BackColor = P.Input;
                        tv.ForeColor = P.Text;
                        tv.LineColor = P.Border;
                        tv.BorderStyle = BorderStyle.FixedSingle;
                        StyleScroll(tv);
                        break;
                    case ToolStrip strip:
                        StyleStrip(strip);
                        break;
                    case LinkLabel link:
                        link.BackColor = P.Window;
                        link.ForeColor = P.Text;
                        link.LinkColor = P.AccentText;
                        link.ActiveLinkColor = P.AccentText;
                        link.VisitedLinkColor = P.SubText;
                        break;
                    case Label label:
                        label.BackColor = P.Window;
                        label.ForeColor = label is ValidationMessageLabel ? P.Error : P.Text;
                        break;
                    case CheckBox check:
                        check.BackColor = P.Window;
                        check.ForeColor = P.Text;
                        check.FlatStyle = FlatStyle.Flat;
                        check.FlatAppearance.BorderSize = 1;
                        check.FlatAppearance.BorderColor = check.Focused ? P.Accent : P.InputBorder;
                        check.FlatAppearance.MouseOverBackColor = P.Hover;
                        check.FlatAppearance.MouseDownBackColor = P.Pressed;
                        check.GotFocus -= CheckRadioFocusChanged;
                        check.LostFocus -= CheckRadioFocusChanged;
                        check.GotFocus += CheckRadioFocusChanged;
                        check.LostFocus += CheckRadioFocusChanged;
                        break;
                    case RadioButton radio:
                        radio.BackColor = P.Window;
                        radio.ForeColor = P.Text;
                        radio.FlatStyle = FlatStyle.Flat;
                        radio.FlatAppearance.BorderSize = 1;
                        radio.FlatAppearance.BorderColor = radio.Focused ? P.Accent : P.InputBorder;
                        radio.FlatAppearance.MouseOverBackColor = P.Hover;
                        radio.FlatAppearance.MouseDownBackColor = P.Pressed;
                        radio.GotFocus -= CheckRadioFocusChanged;
                        radio.LostFocus -= CheckRadioFocusChanged;
                        radio.GotFocus += CheckRadioFocusChanged;
                        radio.LostFocus += CheckRadioFocusChanged;
                        break;
                    case GroupBox group:
                        group.BackColor = P.Window;
                        group.ForeColor = P.Text;
                        break;
                    default:
                        c.BackColor = P.Window;
                        c.ForeColor = P.Text;
                        break;
                }
                AttachInputBorder(c);
                if (c.HasChildren) ApplyChildren(c);
            }
        }

        static void StyleButton(Button button, bool primary)
        {
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = primary ? P.Accent : P.Button;
            button.ForeColor = primary ? ContrastText(P.Accent) : P.Text;
            button.FlatAppearance.BorderSize = button.Focused ? 2 : 1;
            button.FlatAppearance.BorderColor = button.Focused ? P.Accent : primary ? P.Accent : P.InputBorder;
            button.FlatAppearance.MouseOverBackColor = primary ? P.Accent : P.Hover;
            button.FlatAppearance.MouseDownBackColor = primary ? P.Accent : P.Pressed;
            button.GotFocus -= ButtonFocusChanged;
            button.LostFocus -= ButtonFocusChanged;
            button.Paint -= DrawButtonFocus;
            button.GotFocus += ButtonFocusChanged;
            button.LostFocus += ButtonFocusChanged;
            button.Paint += DrawButtonFocus;
        }

        public static void StyleDialogButton(Button button, bool isPrimary) => StyleButton(button, isPrimary);

        static void CheckRadioFocusChanged(object? sender, EventArgs e)
        {
            if (sender is CheckBox check)
            {
                check.FlatAppearance.BorderColor = check.Focused ? P.Accent : P.InputBorder;
                check.Invalidate();
            }
            else if (sender is RadioButton radio)
            {
                radio.FlatAppearance.BorderColor = radio.Focused ? P.Accent : P.InputBorder;
                radio.Invalidate();
            }
        }

        static void AttachInputBorder(Control control)
        {
            if (control is TextBoxBase && control.Parent is ValidationInputFrame) return;
            if (control is TextBoxBase or ComboBox or NumericUpDown or CheckedListBox or ListBox or TreeView)
                _ = InputBorders.GetValue(control, c => new InputBorderWindow(c));
        }

        sealed class InputBorderWindow : NativeWindow
        {
            const int WmPaint = 0x000F, WmNcPaint = 0x0085, WmSetFocus = 0x0007,
                WmKillFocus = 0x0008, WmEnable = 0x000A, WmThemeChanged = 0x031A;
            readonly Control control;

            public InputBorderWindow(Control control)
            {
                this.control = control;
                control.HandleCreated += HandleCreated;
                control.HandleDestroyed += HandleDestroyed;
                control.Disposed += ControlDisposed;
                if (control.IsHandleCreated) AssignHandle(control.Handle);
            }

            void HandleCreated(object? sender, EventArgs e)
            {
                if (control.IsHandleCreated && Handle != control.Handle)
                {
                    if (Handle != IntPtr.Zero) ReleaseHandle();
                    AssignHandle(control.Handle);
                }
                DrawBorder();
            }

            void HandleDestroyed(object? sender, EventArgs e)
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
            }

            void ControlDisposed(object? sender, EventArgs e)
            {
                control.HandleCreated -= HandleCreated;
                control.HandleDestroyed -= HandleDestroyed;
                control.Disposed -= ControlDisposed;
                if (Handle != IntPtr.Zero) ReleaseHandle();
            }

            protected override void WndProc(ref Message m)
            {
                int message = m.Msg;
                base.WndProc(ref m);
                if (message is WmPaint or WmNcPaint or WmSetFocus or WmKillFocus or WmEnable or WmThemeChanged)
                    DrawBorder();
            }

            void DrawBorder()
            {
                IntPtr hwnd = Handle;
                if (hwnd == IntPtr.Zero || control.IsDisposed || !control.IsHandleCreated) return;
                IntPtr hdc = GetWindowDC(hwnd);
                if (hdc == IntPtr.Zero) return;
                try
                {
                    if (!GetWindowRect(hwnd, out NativeRect rect)) return;
                    int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
                    if (width < 2 || height < 2) return;
                    using var graphics = Graphics.FromHdc(hdc);
                    int thickness = control.ContainsFocus ? Math.Max(2, Scale(control, 2)) : 1;
                    using var pen = new Pen(control.ContainsFocus ? P.Accent : P.InputBorder, thickness);
                    graphics.DrawRectangle(pen, thickness / 2f, thickness / 2f,
                        width - thickness, height - thickness);
                }
                catch (Exception exIgnored) { AppLog.Write("Theme input border (ignored): " + exIgnored.Message); }
                finally { _ = ReleaseDC(hwnd, hdc); }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        static void ButtonFocusChanged(object? sender, EventArgs e)
        {
            if (sender is Button button)
            {
                button.FlatAppearance.BorderSize = button.Focused ? 2 : 1;
                button.FlatAppearance.BorderColor = button.Focused ? P.Accent : P.InputBorder;
                button.Invalidate();
            }
        }

        static void DrawButtonFocus(object? sender, PaintEventArgs e)
        {
            if (sender is Button button && button.Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(button.ClientRectangle, -4, -4), button.ForeColor, button.BackColor);
        }

        static void DrawComboItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not ComboBox combo || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back = selected ? P.Accent : P.Input;
            Color fore = selected ? ContrastText(P.Accent) : P.Text;
            using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            string text = (e.Index >= 0 && e.Index < combo.Items.Count
                ? combo.GetItemText(combo.Items[e.Index])
                : combo.Text) ?? string.Empty;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, Rectangle.Inflate(e.Bounds, -Scale(combo, 6), 0),
                fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        static void DrawListItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not ListBox list || e.Index < 0 || e.Index >= list.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back = selected ? P.Selection : P.Input;
            using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, list.GetItemText(list.Items[e.Index]), list.Font,
                Rectangle.Inflate(e.Bounds, -Scale(list, 5), 0), P.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        static void DrawCheckedListItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not CheckedListBox list || e.Index < 0 || e.Index >= list.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back = selected ? P.Selection : P.Input;
            using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            int box = Scale(list, 14);
            var check = new Rectangle(e.Bounds.X + Scale(list, 5), e.Bounds.Y + (e.Bounds.Height - box) / 2, box, box);
            using (var brush = new SolidBrush(P.Window)) e.Graphics.FillRectangle(brush, check);
            using (var pen = new Pen(P.Border)) e.Graphics.DrawRectangle(pen, check);
            if (list.GetItemChecked(e.Index))
            {
                using var pen = new Pen(P.Accent, Math.Max(1, Scale(list, 2)));
                e.Graphics.DrawLines(pen, new[] {
                    new Point(check.X + Scale(list, 3), check.Y + box / 2),
                    new Point(check.X + box / 2 - 1, check.Bottom - Scale(list, 4)),
                    new Point(check.Right - Scale(list, 2), check.Y + Scale(list, 3))
                });
            }
            TextRenderer.DrawText(e.Graphics, list.GetItemText(list.Items[e.Index]), list.Font,
                new Rectangle(check.Right + Scale(list, 6), e.Bounds.Y, e.Bounds.Width - check.Width - Scale(list, 16), e.Bounds.Height),
                P.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
    }

    // ====================== ToolStrip Color Table ======================
    public class ThemeColorTable : ProfessionalColorTable
    {
        static Palette p => Theme.P;

        public ThemeColorTable() { UseSystemColors = false; }

        public override Color ToolStripGradientBegin => p.Toolbar;
        public override Color ToolStripGradientMiddle => p.Toolbar;
        public override Color ToolStripGradientEnd => p.Toolbar;
        public override Color ToolStripBorder => p.Border;
        public override Color ToolStripDropDownBackground => p.Input;
        public override Color ToolStripContentPanelGradientBegin => p.Toolbar;
        public override Color ToolStripContentPanelGradientEnd => p.Toolbar;
        public override Color ToolStripPanelGradientBegin => p.Toolbar;
        public override Color ToolStripPanelGradientEnd => p.Toolbar;
        public override Color StatusStripGradientBegin => p.Toolbar;
        public override Color StatusStripGradientEnd => p.Toolbar;
        public override Color MenuStripGradientBegin => p.Toolbar;
        public override Color MenuStripGradientEnd => p.Toolbar;

        public override Color MenuBorder => p.Border;
        public override Color MenuItemBorder => p.Border;
        public override Color MenuItemSelected => p.Hover;
        public override Color MenuItemSelectedGradientBegin => p.Hover;
        public override Color MenuItemSelectedGradientEnd => p.Hover;
        public override Color MenuItemPressedGradientBegin => p.Pressed;
        public override Color MenuItemPressedGradientMiddle => p.Pressed;
        public override Color MenuItemPressedGradientEnd => p.Pressed;

        public override Color ImageMarginGradientBegin => p.Input;
        public override Color ImageMarginGradientMiddle => p.Input;
        public override Color ImageMarginGradientEnd => p.Input;

        public override Color ButtonSelectedHighlight => p.Hover;
        public override Color ButtonSelectedHighlightBorder => p.Border;
        public override Color ButtonSelectedBorder => p.Border;
        public override Color ButtonSelectedGradientBegin => p.Hover;
        public override Color ButtonSelectedGradientMiddle => p.Hover;
        public override Color ButtonSelectedGradientEnd => p.Hover;

        public override Color ButtonPressedHighlight => p.Pressed;
        public override Color ButtonPressedHighlightBorder => p.Border;
        public override Color ButtonPressedBorder => p.Border;
        public override Color ButtonPressedGradientBegin => p.Pressed;
        public override Color ButtonPressedGradientMiddle => p.Pressed;
        public override Color ButtonPressedGradientEnd => p.Pressed;

        public override Color ButtonCheckedHighlight => p.Pressed;
        public override Color ButtonCheckedHighlightBorder => p.Border;
        public override Color ButtonCheckedGradientBegin => p.Pressed;
        public override Color ButtonCheckedGradientMiddle => p.Pressed;
        public override Color ButtonCheckedGradientEnd => p.Pressed;

        public override Color SeparatorDark => p.Border;
        public override Color SeparatorLight => p.Border;
        public override Color GripDark => p.Border;
        public override Color GripLight => p.Border;
    }

    // ====================== Icons (Segoe Fluent Icons / MDL2 glyphs) ======================
    // The font is already included with Windows, so it does not increase the application size.
    public static class Icons
    {
        static readonly string? Family = PickFamily();

        static string? PickFamily()
        {
            try
            {
                using var fc = new InstalledFontCollection();
                foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                    if (fc.Families.Any(f => f.Name == name)) return name;
            }
            catch (Exception exIgnored3) { AppLog.Write("Theme (ignored): " + exIgnored3.Message); }
            return null;
        }

        public static bool Available => Family != null;

        public static Bitmap? Make(string glyph, Color color, int size = 20)
        {
            if (Family == null) return null;
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = new Font(Family, size * 0.85f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), sf);
            return bmp;
        }
    }
}
