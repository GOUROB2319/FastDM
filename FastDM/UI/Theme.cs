using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastDM
{
    public enum ThemeMode { System, Light, Dark }

    public class Palette
    {
        public Color Window, RowAlt, Text, SubText, Border, Accent, Selection, Track,
                     Header, Toolbar, Hover, Pressed, Input, Button,
                     SideBg, SideSel, SideText, SideCount;
    }

    // ====================== Theme Manager ======================
    public static class Theme
    {
        public static readonly Palette Light = new Palette
        {
            Window = ColorTranslator.FromHtml("#FFFFFF"),
            RowAlt = ColorTranslator.FromHtml("#F8F9FC"),
            Text = ColorTranslator.FromHtml("#2D3142"),
            SubText = ColorTranslator.FromHtml("#6B7089"),
            Border = ColorTranslator.FromHtml("#E3E6EF"),
            Accent = ColorTranslator.FromHtml("#5B6CFF"),
            Selection = ColorTranslator.FromHtml("#DEE6FF"),
            Track = ColorTranslator.FromHtml("#E6E9F2"),
            Header = ColorTranslator.FromHtml("#F4F5FA"),
            Toolbar = ColorTranslator.FromHtml("#FFFFFF"),
            Hover = ColorTranslator.FromHtml("#EDEFF7"),
            Pressed = ColorTranslator.FromHtml("#DCE0F0"),
            Input = ColorTranslator.FromHtml("#FFFFFF"),
            Button = ColorTranslator.FromHtml("#F4F5FA"),
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
            SubText = ColorTranslator.FromHtml("#9AA0B4"),
            Border = ColorTranslator.FromHtml("#2C3043"),
            Accent = ColorTranslator.FromHtml("#7482FF"),
            Selection = ColorTranslator.FromHtml("#2A3157"),
            Track = ColorTranslator.FromHtml("#2C3043"),
            Header = ColorTranslator.FromHtml("#1F2230"),
            Toolbar = ColorTranslator.FromHtml("#1F2230"),
            Hover = ColorTranslator.FromHtml("#2A2E42"),
            Pressed = ColorTranslator.FromHtml("#343958"),
            Input = ColorTranslator.FromHtml("#1F2230"),
            Button = ColorTranslator.FromHtml("#262A3B"),
            SideBg = ColorTranslator.FromHtml("#11131B"),
            SideSel = ColorTranslator.FromHtml("#23283D"),
            SideText = ColorTranslator.FromHtml("#FFFFFF"),
            SideCount = ColorTranslator.FromHtml("#8C93B3")
        };

        public static ThemeMode Mode { get; private set; } = ThemeMode.System;
        public static Palette P { get; private set; } = Light;
        public static bool IsDark { get; private set; }

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
                catch { }
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
                catch { }
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
        }

        // ---------- Apply theme to all controls in the form/dialog ----------
        public static void Apply(Form f)
        {
            f.BackColor = P.Window;
            f.ForeColor = P.Text;
            SetTitleBar(f);
            ApplyChildren(f);
        }

        static void ApplyChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                switch (c)
                {
                    case Button b:
                        b.UseVisualStyleBackColor = false;
                        b.FlatStyle = FlatStyle.Flat;
                        b.BackColor = P.Button;
                        b.ForeColor = P.Text;
                        b.FlatAppearance.BorderColor = P.Border;
                        b.FlatAppearance.MouseOverBackColor = P.Hover;
                        b.FlatAppearance.MouseDownBackColor = P.Pressed;
                        break;
                    case TextBox t:
                        t.BackColor = P.Input;
                        t.ForeColor = P.Text;
                        t.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case ComboBox cb:
                        cb.BackColor = P.Input;
                        cb.ForeColor = P.Text;
                        cb.FlatStyle = FlatStyle.Flat;
                        break;
                    case NumericUpDown n:
                        n.BackColor = P.Input;
                        n.ForeColor = P.Text;
                        break;
                    case TreeView tv:
                        tv.BackColor = P.Input;
                        tv.ForeColor = P.Text;
                        tv.LineColor = P.Border;
                        tv.BorderStyle = BorderStyle.FixedSingle;
                        StyleScroll(tv);
                        break;
                    default:
                        c.BackColor = P.Window;
                        c.ForeColor = P.Text;
                        break;
                }
                if (c.HasChildren) ApplyChildren(c);
            }
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
            catch { }
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
