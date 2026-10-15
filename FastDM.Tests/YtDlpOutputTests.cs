namespace FastDM.Tests
{
    // Downloads named after the video title: find the finished file in the private folder, move it out.
    static class YtDlpOutputTests
    {
        static string NewDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "fastdm-out-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static void Run()
        {
            T.Section("Title-named downloads: finding and moving the result");
            var dirs = new List<string>();
            string Fresh() { var d = NewDir(); dirs.Add(d); return d; }

            try
            {
                string dir = Fresh();
                T.Check("empty folder -> nothing", YtDlpOutput.FindFinal(dir) == null);
                T.Check("missing folder -> nothing", YtDlpOutput.FindFinal(Path.Combine(dir, "nope")) == null);

                File.WriteAllText(Path.Combine(dir, "Video.f137.mp4"), "v");
                File.WriteAllText(Path.Combine(dir, "Video.f140.m4a"), "a");
                File.WriteAllText(Path.Combine(dir, "Video.mp4.part"), "p");
                File.WriteAllText(Path.Combine(dir, "Video.mp4.ytdl"), "y");
                File.WriteAllText(Path.Combine(dir, "Video.temp"), "t");
                T.Check("only half-finished files -> nothing", YtDlpOutput.FindFinal(dir) == null);

                string bangla = Path.Combine(dir, "রামায়ণ গান - Ramayana.mp4");
                File.WriteAllText(bangla, "done");
                T.Check("the finished file is found, other pieces ignored", YtDlpOutput.FindFinal(dir) == bangla);

                string empty = Path.Combine(dir, "empty.mp4");
                File.WriteAllBytes(empty, Array.Empty<byte>());
                File.SetLastWriteTimeUtc(empty, DateTime.UtcNow.AddMinutes(1));
                T.Check("a newer empty output does not replace a finished non-empty file", YtDlpOutput.FindFinal(dir) == bangla);

                string newer = Path.Combine(dir, "Second.m4a");
                File.WriteAllText(newer, "x");
                File.SetLastWriteTimeUtc(bangla, DateTime.UtcNow - TimeSpan.FromMinutes(5));
                T.Check("with two finished files the newest wins", YtDlpOutput.FindFinal(dir) == newer);

                string target = Fresh();
                string moved = YtDlpOutput.MoveToUnique(bangla, target);
                T.Check("moved into the target folder, name kept (Bangla title)", moved == Path.Combine(target, "রামায়ণ গান - Ramayana.mp4") && File.Exists(moved) && !File.Exists(bangla));

                File.WriteAllText(bangla, "again");
                string moved2 = YtDlpOutput.MoveToUnique(bangla, target);
                File.WriteAllText(bangla, "third");
                string moved3 = YtDlpOutput.MoveToUnique(bangla, target);
                T.Check("name taken -> (1), (2) added, nothing overwritten",
                    Path.GetFileName(moved2) == "রামায়ণ গান - Ramayana (1).mp4" && Path.GetFileName(moved3) == "রামায়ণ গান - Ramayana (2).mp4"
                    && File.ReadAllText(moved) == "done" && File.ReadAllText(moved2) == "again");

                string made = YtDlpOutput.MoveToUnique(newer, Path.Combine(Fresh(), "new", "sub"));
                T.Check("target folder is created when missing", File.Exists(made));

                T.Check("private folder name is short and stable", YtDlpOutput.ItemDirName("0123456789abcdef") == ".fastdm-01234567" && YtDlpOutput.ItemDirName("abc") == ".fastdm-abc");
            }
            finally
            {
                foreach (var d in dirs)
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
        }
    }
}
