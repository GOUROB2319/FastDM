using System.Text.Json;

namespace FastDM.Tests
{
    // state.json holds the whole download list: a damaged file must never silently wipe it.
    static class StateStoreTests
    {
        sealed class Doc
        {
            public string Name { get; set; } = "";
            public int SchemaVersion { get; set; }
        }

        static Doc? Parse(string text) => JsonSerializer.Deserialize<Doc>(text);

        static string NewDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "fastdm-state-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static string Json(string name) => "{\"Name\":\"" + name + "\"}";
        static string[] Files(string dir, string pattern) => Directory.GetFiles(dir, pattern);

        public static void Run()
        {
            var dirs = new List<string>();
            string Fresh() { var d = NewDir(); dirs.Add(d); return d; }

            try
            {
                ReadCases(Fresh);
                WriteCases(Fresh);
                OtherCases(Fresh);
            }
            finally
            {
                foreach (var d in dirs)
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
        }

        static void ReadCases(Func<string> fresh)
        {
            T.Section("state.json: loading");

            {   // no file yet
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                var r = StateStore.Read(file, Parse);
                T.Check("first run (no file): empty, no warning, nothing created", r.Data == null && r.Notice == null && Directory.GetFiles(dir).Length == 0);
            }
            {   // healthy file
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                File.WriteAllText(file, Json("ok"));
                var r = StateStore.Read(file, Parse);
                T.Check("healthy file: loaded, no warning, no extra files", r.Data?.Name == "ok" && r.Notice == null && !r.FromBackup && Directory.GetFiles(dir).Length == 1);
            }
            {   // damaged, no backup
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                string damaged = "{\"Name\":\"my precious list\"";          // cut off in the middle
                File.WriteAllText(file, damaged);
                var r = StateStore.Read(file, Parse);
                var kept = Files(dir, "state.json.damaged-*");
                T.Check("damaged, no backup: starts empty and says so", r.Data == null && r.Notice != null && r.Notice.Contains("empty list") && !r.KeepBlocked);
                T.Check("damaged file is kept, content untouched", kept.Length == 1 && File.ReadAllText(kept[0]) == damaged);
                T.Check("notice tells the user where the copy is", r.Notice != null && kept.Length > 0 && r.Notice.Contains(kept[0]));
                T.Check("damaged file no longer sits at state.json", !File.Exists(file));
            }
            {   // damaged, good .bak
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                File.WriteAllText(file, "{ not json");
                File.WriteAllText(StateStore.Backup1(file), Json("from-backup"));
                var r = StateStore.Read(file, Parse);
                T.Check("damaged + good .bak: restored from backup", r.Data?.Name == "from-backup" && r.FromBackup && r.Notice != null && r.Notice.Contains("restored"));
                T.Check("restored copy is put back as state.json", File.Exists(file) && File.ReadAllText(file) == Json("from-backup"));
                T.Check("damaged original also kept", Files(dir, "state.json.damaged-*").Length == 1);
            }
            {   // damaged .bak, good .bak2
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                File.WriteAllText(file, "garbage");
                File.WriteAllText(StateStore.Backup1(file), "also garbage");
                File.WriteAllText(StateStore.Backup2(file), Json("second"));
                var r = StateStore.Read(file, Parse);
                T.Check("bad .bak: falls back to .bak2", r.Data?.Name == "second" && r.FromBackup);
                T.Check("bad .bak is set aside, not reused", Files(dir, "state.json.bak.damaged-*").Length == 1 && !File.Exists(StateStore.Backup1(file)));
            }
            {   // everything damaged
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                File.WriteAllText(file, "x"); File.WriteAllText(StateStore.Backup1(file), "y"); File.WriteAllText(StateStore.Backup2(file), "z");
                var r = StateStore.Read(file, Parse);
                T.Check("all three damaged: starts empty, all kept", r.Data == null && r.Notice != null && Files(dir, "*.damaged-*").Length == 3);
            }
            foreach (var (label, text) in new[] { ("empty file", ""), ("blank file", "  \r\n "), ("JSON null", "null") })
            {
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                File.WriteAllText(file, text);
                var r = StateStore.Read(file, Parse);
                T.Check("treated as damaged: " + label, r.Data == null && r.Notice != null && Files(dir, "state.json.damaged-*").Length == 1);
            }
            {   // unknown extra fields from a newer version are fine
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                File.WriteAllText(file, "{\"Name\":\"x\",\"SchemaVersion\":9,\"SomethingNew\":{\"a\":1}}");
                var r = StateStore.Read(file, Parse);
                T.Check("file with unknown newer fields still loads", r.Data?.Name == "x" && r.Data.SchemaVersion == 9 && r.Notice == null);
            }
            {   // only the newest 5 damaged copies are kept
                string dir = fresh(), file = Path.Combine(dir, "state.json");
                for (int i = 1; i <= 8; i++)
                {
                    File.WriteAllText(file, "broken " + i);
                    StateStore.Read(file, Parse);
                }
                var kept = Files(dir, "state.json.damaged-*").OrderBy(f => f, StringComparer.Ordinal).ToArray();
                T.Check("only the newest 5 damaged copies are kept", kept.Length == StateStore.KeepDamagedCopies);
                T.Check("the newest damaged copy survives the cleanup", kept.Length > 0 && File.ReadAllText(kept[^1]) == "broken 8");
            }
        }

        static void WriteCases(Func<string> fresh)
        {
            T.Section("state.json: saving and backups");

            string dir = fresh(), file = Path.Combine(dir, "state.json");
            string b1 = StateStore.Backup1(file), b2 = StateStore.Backup2(file);

            StateStore.Write(file, Json("one"));
            T.Check("save writes state.json and leaves no .tmp file", File.ReadAllText(file) == Json("one") && Files(dir, "*.tmp").Length == 0);
            T.Check("first save also creates .bak", File.Exists(b1) && File.ReadAllText(b1) == Json("one") && !File.Exists(b2));

            StateStore.Write(file, Json("two"));
            T.Check("second save soon after: .bak untouched", File.ReadAllText(file) == Json("two") && File.ReadAllText(b1) == Json("one") && !File.Exists(b2));

            File.SetLastWriteTimeUtc(b1, DateTime.UtcNow - TimeSpan.FromMinutes(20));
            StateStore.Write(file, Json("three"));
            T.Check("backup older than 15 min: .bak2 = previous .bak, .bak = newest", File.ReadAllText(b2) == Json("one") && File.ReadAllText(b1) == Json("three"));
            T.Check("no leftover .tmp files after backups", Files(dir, "*.tmp").Length == 0);

            // the full round trip a real crash would need: save, damage, load
            File.WriteAllText(file, "{\"Name\":\"th");                       // power cut in the middle of a write
            var r = StateStore.Read(file, Parse);
            T.Check("round trip: damaged after a save -> recovered from the last backup", r.Data?.Name == "three" && r.FromBackup);
        }

        static void OtherCases(Func<string> fresh)
        {
            T.Section("state.json: saved by a newer version");

            string dir = fresh(), file = Path.Combine(dir, "state.json");
            File.WriteAllText(file, "{\"Name\":\"new\",\"SchemaVersion\":7}");
            string? kept = StateStore.KeepNewerCopy(file, 7);
            T.Check("untouched copy kept as state.json.from-v7", kept == file + ".from-v7" && File.ReadAllText(kept) == "{\"Name\":\"new\",\"SchemaVersion\":7}");

            File.WriteAllText(file, "{\"Name\":\"changed\"}");
            string? again = StateStore.KeepNewerCopy(file, 7);
            T.Check("the copy is made only once (never overwritten)", again == kept && File.ReadAllText(kept!) == "{\"Name\":\"new\",\"SchemaVersion\":7}");
            T.Check("no file -> nothing kept", StateStore.KeepNewerCopy(Path.Combine(dir, "missing.json"), 3) == null);
        }
    }
}
