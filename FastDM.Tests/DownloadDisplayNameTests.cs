namespace FastDM.Tests
{
    static class DownloadDisplayNameTests
    {
        public static void Run()
        {
            T.Section("UI filename display safety");

            T.Check("path is reduced to a readable filename", DownloadDisplayName.Format("C:\\Downloads\\video.mp4") == "video.mp4");
            T.Check("Windows-invalid characters become spaces", DownloadDisplayName.Format("a?b|c:d.txt") == "a b c d.txt");
            T.Check("whitespace is collapsed", DownloadDisplayName.Format("  a?   b.txt  ") == "a b.txt");
            T.Check("empty names get a readable fallback", DownloadDisplayName.Format(null) == "Unnamed download");
            T.Check("long display names are capped", DownloadDisplayName.Format(new string('x', 241)).Length == 240);
        }
    }
}
