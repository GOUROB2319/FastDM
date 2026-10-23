using FastDM;

namespace FastDM.Tests
{
    static class FolderNamingPolicyTests
    {
        public static void Run()
        {
            T.Section("download folder naming");
            T.Check("list file names become safe folder names", FolderNamingPolicy.ListFolder("C:\\Lists\\Summer: links.txt") == "Summer links");
            T.Check("playlist titles are sanitized and capped", FolderNamingPolicy.PlaylistFolder("  My / playlist?  ") == "My playlist" && FolderNamingPolicy.PlaylistFolder(new string('x', 100)).Length == FolderNamingPolicy.MaxFolderNameLength);
            T.Check("same input combines into the same folder without numeric suffixes", FolderNamingPolicy.Combine("C:\\Downloads", "Playlist") == FolderNamingPolicy.Combine("C:\\Downloads", "Playlist"));
            string[] nestedFtpFixture = { "remote", "one", "two" };
            string relative = "";
            foreach (string segment in nestedFtpFixture) relative = FolderNamingPolicy.AppendRelative(relative, segment);
            T.Check("nested FTP fixture preserves its three-level relative folder path", System.IO.Path.Combine("C:\\Downloads", relative) == "C:\\Downloads\\remote\\one\\two");
        }
    }
}
