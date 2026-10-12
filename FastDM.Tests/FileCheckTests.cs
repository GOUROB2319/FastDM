namespace FastDM.Tests
{
    // A completed download whose file is gone moves to the Deleted list, but never because a drive is unplugged.
    static class FileCheckTests
    {
        public static void Run()
        {
            T.Section("Deleted files: how we decide");

            string dir = Path.Combine(Path.GetTempPath(), "fastdm-fc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                string name = "রামায়ণ.mp4";
                File.WriteAllText(Path.Combine(dir, name), "x");

                T.Check("file is there -> not deleted", !FileCheck.IsDeleted(dir, name));
                File.Delete(Path.Combine(dir, name));
                T.Check("file gone, folder there -> deleted", FileCheck.IsDeleted(dir, name));
                T.Check("whole folder gone, drive there -> deleted", FileCheck.IsDeleted(Path.Combine(dir, "removed", "sub"), "a.mp4"));
                T.Check("empty folder or name -> not deleted", !FileCheck.IsDeleted("", "a.mp4") && !FileCheck.IsDeleted(dir, "") && !FileCheck.IsDeleted(null, null));
                T.Check("illegal characters in the path -> not deleted (no guessing)", !FileCheck.IsDeleted(dir + "\0bad", "a.mp4"));

                if (OperatingSystem.IsWindows())
                {
                    var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
                    char free = "QRSTUVWXYZ".FirstOrDefault(c => !used.Contains(c));

                    if (free != default)
                        T.Check("drive not connected -> NOT deleted", !FileCheck.IsDeleted(free + @":\Downloads", "a.mp4"));
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
