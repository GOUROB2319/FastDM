using System.Drawing;

namespace FastDM.Tests;

static class ThemeContrastTests
{
    static readonly (string Name, Func<Palette, Color> Color)[] TextColors =
    {
        ("Text", p => p.Text),
        ("SubText", p => p.SubText),
        ("AccentText", p => p.AccentText),
        ("Success", p => p.Success),
        ("Warning", p => p.Warning),
        ("Error", p => p.Error)
    };

    static readonly (string Name, Func<Palette, Color> Color)[] Surfaces =
    {
        ("Window", p => p.Window),
        ("RowAlt", p => p.RowAlt),
        ("Header", p => p.Header),
        ("Toolbar", p => p.Toolbar),
        ("Hover", p => p.Hover),
        ("Pressed", p => p.Pressed),
        ("Selection", p => p.Selection),
        ("Input", p => p.Input),
        ("Button", p => p.Button)
    };

    public static void Run()
    {
        T.Section("Theme contrast ratios");
        CheckPalette("Light", Theme.Light);
        CheckPalette("Dark", Theme.Dark);
    }

    static void CheckPalette(string name, Palette palette)
    {
        foreach (var foreground in TextColors)
        foreach (var surface in Surfaces)
        {
            double ratio = Theme.ContrastRatio(foreground.Color(palette), surface.Color(palette));
            T.Check($"{name} {foreground.Name} on {surface.Name} >= 4.5:1 ({ratio:F2})", ratio >= 4.5);
        }

        foreach (var surface in Surfaces)
        {
            double border = Theme.ContrastRatio(palette.InputBorder, surface.Color(palette));
            T.Check($"{name} InputBorder on {surface.Name} >= 3:1 ({border:F2})", border >= 3.0);

            double focus = Theme.ContrastRatio(palette.Accent, surface.Color(palette));
            T.Check($"{name} focus ring on {surface.Name} >= 3:1 ({focus:F2})", focus >= 3.0);

            double fill = Theme.ContrastRatio(palette.Accent, surface.Color(palette));
            T.Check($"{name} Accent fill on {surface.Name} >= 3:1 ({fill:F2})", fill >= 3.0);
        }

        foreach (var sideSurface in new[] { palette.SideBg, palette.SideSel })
        {
            double sideText = Theme.ContrastRatio(palette.SideText, sideSurface);
            double sideCount = Theme.ContrastRatio(palette.SideCount, sideSurface);
            T.Check($"{name} sidebar text >= 4.5:1 ({sideText:F2})", sideText >= 4.5);
            T.Check($"{name} sidebar count >= 4.5:1 ({sideCount:F2})", sideCount >= 4.5);
        }

        Color primaryText = Theme.ContrastText(palette.Accent);
        double primaryRatio = Theme.ContrastRatio(primaryText, palette.Accent);
        T.Check($"{name} primary-button text >= 4.5:1 ({primaryRatio:F2})", primaryRatio >= 4.5);
    }
}
