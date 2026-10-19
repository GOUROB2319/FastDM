namespace FastDM.Tests;

static class DownloadRowRefreshPolicyTests
{
    public static void Run()
    {
        T.Section("Download row redraw decisions");
        T.Check("unchanged cell text does not report a change",
            !FastDM.DownloadRowRefreshPolicy.CellChanged("42 MB", "42 MB"));
        T.Check("changed cell text reports a change",
            FastDM.DownloadRowRefreshPolicy.CellChanged("42 MB", "43 MB"));
        T.Check("unchanged cells and progress pixels skip redraw",
            !FastDM.DownloadRowRefreshPolicy.ShouldRedraw(false, 52, 52));
        T.Check("changed progress pixels redraw even when text is unchanged",
            FastDM.DownloadRowRefreshPolicy.ShouldRedraw(false, 52, 53));
        T.Check("changed text redraws the row",
            FastDM.DownloadRowRefreshPolicy.ShouldRedraw(true, 52, 52));
    }
}
