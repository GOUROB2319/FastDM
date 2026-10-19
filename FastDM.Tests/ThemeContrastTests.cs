namespace FastDM.Tests;

static class ThemeContrastTests
{
    public static void Run()
    {
        T.Section("Disabled control contrast");
        T.Check("light theme disabled text meets 3:1 on Window",
            FastDM.ThemeContrastPolicy.ContrastRatio(FastDM.ThemeContrastPolicy.LightDisabledText, FastDM.ThemeContrastPolicy.LightWindow) >= 3.0);
        T.Check("dark theme disabled text meets 3:1 on Window",
            FastDM.ThemeContrastPolicy.ContrastRatio(FastDM.ThemeContrastPolicy.DarkDisabledText, FastDM.ThemeContrastPolicy.DarkWindow) >= 3.0);
        T.Check("light theme enabled text meets 4.5:1 on Window",
            FastDM.ThemeContrastPolicy.ContrastRatio(FastDM.ThemeContrastPolicy.LightText, FastDM.ThemeContrastPolicy.LightWindow) >= 4.5);
        T.Check("dark theme enabled text meets 4.5:1 on Window",
            FastDM.ThemeContrastPolicy.ContrastRatio(FastDM.ThemeContrastPolicy.DarkText, FastDM.ThemeContrastPolicy.DarkWindow) >= 4.5);
        T.Check("light status link text meets 4.5:1 on the toolbar",
            FastDM.ThemeContrastPolicy.ContrastRatio(FastDM.ThemeContrastPolicy.LightSubText, FastDM.ThemeContrastPolicy.LightToolbar) >= 4.5);
        T.Check("dark status link text meets 4.5:1 on the toolbar",
            FastDM.ThemeContrastPolicy.ContrastRatio(FastDM.ThemeContrastPolicy.DarkSubText, FastDM.ThemeContrastPolicy.DarkToolbar) >= 4.5);
        T.Check("light disabled button text meets 3:1 on its button face",
            FastDM.ThemeContrastPolicy.ContrastRatio(FastDM.ThemeContrastPolicy.LightDisabledText, FastDM.ThemeContrastPolicy.LightButton) >= 3.0);
        T.Check("dark disabled button text meets 3:1 on its button face",
            FastDM.ThemeContrastPolicy.ContrastRatio(FastDM.ThemeContrastPolicy.DarkDisabledText, FastDM.ThemeContrastPolicy.DarkButton) >= 3.0);
    }
}
