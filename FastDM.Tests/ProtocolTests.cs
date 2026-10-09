namespace FastDM.Tests
{
    // fastdm:// links can be triggered by ANY web page, so only safe, well-formed commands may get through.
    static class ProtocolTests
    {
        public static void Run()
        {
            T.Section("fastdm:// protocol parsing");

            var add = ProtocolHandler.Parse("fastdm://add?url=" + Uri.EscapeDataString("https://a.test/f.zip?x=1&y=2"));
            T.Check("add: action and link", add?.Action == "add" && add.Url == "https://a.test/f.zip?x=1&y=2");

            var quoted = ProtocolHandler.Parse("\"fastdm://open\"");
            T.Check("open: surrounding quotes tolerated", quoted?.Action == "open" && quoted.Url == null);

            var pl = ProtocolHandler.Parse("fastdm://playlist?url=" + Uri.EscapeDataString("https://a.test/movies/"));
            T.Check("playlist: action and link", pl?.Action == "playlist" && pl.Url == "https://a.test/movies/");

            T.Check("add with javascript: link -> no url", ProtocolHandler.Parse("fastdm://add?url=" + Uri.EscapeDataString("javascript:alert(1)"))?.Url == null);
            T.Check("add with file: link -> no url", ProtocolHandler.Parse("fastdm://add?url=" + Uri.EscapeDataString("file:///c:/x.exe"))?.Url == null);
            T.Check("add without url -> no url", ProtocolHandler.Parse("fastdm://add")?.Url == null);
            T.Check("other scheme -> null", ProtocolHandler.Parse("http://a.test/") == null);
            T.Check("garbage / empty -> null", ProtocolHandler.Parse("not a link") == null && ProtocolHandler.Parse("") == null && ProtocolHandler.Parse("   ") == null);
            T.Check("open ignores a url parameter", ProtocolHandler.Parse("fastdm://open?url=" + Uri.EscapeDataString("https://a.test/x"))?.Url == null);
        }
    }
}
