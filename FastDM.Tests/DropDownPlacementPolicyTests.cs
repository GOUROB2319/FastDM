using System.Drawing;

namespace FastDM.Tests;

static class DropDownPlacementPolicyTests
{
    public static void Run()
    {
        T.Section("Video quality dropdown placement");
        var work = new Rectangle(0, 0, 1280, 800);
        T.Check("dropdown height cap is 40 percent of work area", FastDM.DropDownPlacementPolicy.MaximumHeight(800) == 320);
        var down = FastDM.DropDownPlacementPolicy.Fit(new Rectangle(100, 100, 600, 240), work, 80);
        T.Check("dropdown stays inside the screen below the combo when it fits", down == new Rectangle(100, 100, 600, 240));
        var up = FastDM.DropDownPlacementPolicy.Fit(new Rectangle(100, 700, 600, 500), work, 680);
        T.Check("dropdown opens upward and clamps its height at screen bottom", up == new Rectangle(100, 360, 600, 320));
        var rightEdge = FastDM.DropDownPlacementPolicy.Fit(new Rectangle(900, 100, 600, 240), work, 80);
        T.Check("dropdown width and position stay inside the screen", rightEdge.Right <= work.Right && rightEdge.Left >= work.Left);
    }
}
