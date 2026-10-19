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
        T.Check("name and progress receive useful extra width", normal[0] >= 260 && normal[2] >= 170);
        T.Check("size, speed and ETA retain readable minimum widths", normal[1] >= 84 && normal[4] >= 100 && normal[5] >= 76);
        T.Check("size, speed and ETA use right alignment", FastDM.DownloadColumnLayout.IsRightAligned(1) &&
            FastDM.DownloadColumnLayout.IsRightAligned(4) && FastDM.DownloadColumnLayout.IsRightAligned(5));
        T.Check("name and progress keep left alignment", !FastDM.DownloadColumnLayout.IsRightAligned(0) &&
            !FastDM.DownloadColumnLayout.IsRightAligned(2));

        var highDpi = FastDM.DownloadColumnLayout.Fit(1350, 144);
        T.Check("numeric minimum widths scale with DPI", highDpi[1] >= 126 && highDpi[4] >= 150 && highDpi[5] >= 114);

        var narrow = FastDM.DownloadColumnLayout.Fit(320);
        T.Check("narrow width keeps name and progress visible", narrow[0] > 0 && narrow[2] > 0);
        T.Check("narrow width hides columns that cannot meet minimum widths and fills without scrolling", narrow[1] == 0 && narrow[3] == 0 && narrow[4] == 0 && narrow[5] == 0 && narrow[6] == 0 && narrow.Sum() == 320);

        var resizedBack = FastDM.DownloadColumnLayout.Fit(900);
        T.Check("columns return after the window grows", resizedBack.All(w => w > 0));
        T.Check("zero width produces safe zero-sized columns", FastDM.DownloadColumnLayout.Fit(0).All(w => w == 0));
    }
}
