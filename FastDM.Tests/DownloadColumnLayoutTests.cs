using System.Linq;

namespace FastDM.Tests;

static class DownloadColumnLayoutTests
{
    public static void Run()
    {
        T.Section("Responsive download columns");

        var normal = FastDM.DownloadColumnLayout.Fit(900);
        T.Check("normal width restores every download column", normal.All(w => w > 0));
        T.Check("normal width uses the full available table width", normal.Sum() <= 900 && normal.Sum() >= 896);
        T.Check("name and progress receive the extra width", normal[0] >= 300 && normal[2] >= 170);

        var narrow = FastDM.DownloadColumnLayout.Fit(320);
        T.Check("narrow width keeps name, size and progress visible", narrow[0] > 0 && narrow[1] > 0 && narrow[2] > 0);
        T.Check("narrow width hides secondary columns and fits without scrolling", narrow[3] == 0 && narrow[4] == 0 && narrow[5] == 0 && narrow[6] == 0 && narrow.Sum() <= 320);

        var resizedBack = FastDM.DownloadColumnLayout.Fit(900);
        T.Check("columns return after the window grows", resizedBack.All(w => w > 0));
        T.Check("zero width produces safe zero-sized columns", FastDM.DownloadColumnLayout.Fit(0).All(w => w == 0));
    }
}
