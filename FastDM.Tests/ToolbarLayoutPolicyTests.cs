using System.Linq;

namespace FastDM.Tests;

static class ToolbarLayoutPolicyTests
{
    public static void Run()
    {
        T.Section("Toolbar collapse policy");

        T.Check("secondary actions collapse before Settings text in the requested order",
            FastDM.ToolbarLayoutPolicy.CollapseOrder.SequenceEqual(new[]
            {
                "Details", "Open File", "Open Folder", "Settings and updates"
            }));
        T.Check("Schedule and After downloads always keep their text",
            FastDM.ToolbarLayoutPolicy.MustKeepText("Schedule") &&
            FastDM.ToolbarLayoutPolicy.MustKeepText("After downloads"));
        T.Check("per-item Pause/Resume and Resume/Pause All are not collapsed by the icon-only policy",
            !FastDM.ToolbarLayoutPolicy.CollapseOrder.Contains("Resume") &&
            !FastDM.ToolbarLayoutPolicy.CollapseOrder.Contains("Pause") &&
            !FastDM.ToolbarLayoutPolicy.CollapseOrder.Contains("Resume All") &&
            !FastDM.ToolbarLayoutPolicy.CollapseOrder.Contains("Pause All"));
    }
}
