#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FastDM
{
    // What Read() found. Data == null means "nothing usable, start with an empty list".
    public sealed class StateRead<T> where T : class
    {
        public T? Data;
        public bool FromBackup;
        public bool KeepBlocked;     // the damaged file could not be set aside: do NOT save over it this session
        public string? Notice;       // text to show the user when something went wrong
        public string? Reason;       // technical reason, for the log
    }

    // Safe loading and saving of state.json (settings + download list).
    //   Save : write state.json.tmp, then replace state.json (a crash never leaves a half-written file).
    //          Every 15 minutes the saved text is also kept as state.json.bak (the previous .bak becomes .bak2).
    //   Load : if state.json cannot be read, the damaged file is moved aside (state.json.damaged-<time>),
    //          then FastDM tries .bak and .bak2, and tells the user what happened.
    // Nothing here knows about WinForms, so the logic can be tested on its own.
    public static class StateStore
    {
        public const int CurrentSchema = 2;
        public const int KeepDamagedCopies = 5;
        public static readonly TimeSpan BackupEvery = TimeSpan.FromMinutes(15);

        public static string Backup1(string file) => file + ".bak";
        public static string Backup2(string file) => file + ".bak2";

        static T? TryParse<T>(string path, Func<string, T?> parse, out string? reason) where T : class
        {
            reason = null;
            try
            {
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) { reason = "the file is empty"; return null; }
                var data = parse(text);
                if (data == null) reason = "the file does not contain valid data";
                return data;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return null;
            }
        }

        public static StateRead<T> Read<T>(string file, Func<string, T?> parse) where T : class
        {
            var r = new StateRead<T>();
            if (!File.Exists(file)) return r;

            var data = TryParse(file, parse, out var reason);
            if (data != null) { r.Data = data; return r; }

            // ---- the file is damaged ----
            r.Reason = reason;
            string? kept = SetAside(file);
            r.KeepBlocked = kept == null;

            foreach (var bak in new[] { Backup1(file), Backup2(file) })
            {
                if (!File.Exists(bak)) continue;

                var b = TryParse(bak, parse, out _);
                if (b != null)
                {
                    r.Data = b;
                    r.FromBackup = true;
                    if (!r.KeepBlocked)
                    {
                        try { File.Copy(bak, file, true); } catch (Exception exIgnored1) { AppLog.Write("StateStore (ignored): " + exIgnored1.Message); }     // put the good copy back on disk right away
                    }
                    break;
                }
                SetAside(bak);                                              // a damaged backup is kept for inspection, not reused
            }

            string where = kept != null ? "\n\nA copy of the damaged file was kept here:\n" + kept : "";
            string why = string.IsNullOrWhiteSpace(reason) ? "" : "\n\nDetails: " + (reason!.Length > 200 ? reason.Substring(0, 200) + "…" : reason);

            r.Notice = r.FromBackup
                ? "FastDM could not read its saved download list (state.json), so it was restored from the latest automatic backup. " +
                  "Downloads added in the last few minutes may be missing." + where + why
                : "FastDM could not read its saved download list and settings (state.json), and there was no usable backup, " +
                  "so it started with an empty list." + where + why;

            if (r.KeepBlocked)
                r.Notice += "\n\nThe damaged file could not be set aside, so FastDM will not save over it during this session. " +
                            "Close FastDM, move or fix the file, then start it again.";

            return r;
        }

        // Moves a damaged file to <name>.damaged-<time> (copy as a fallback). Returns the new path, or null if it could not be kept.
        static string? SetAside(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
                string name = Path.GetFileName(path);
                string dest = Path.Combine(dir, name + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                for (int i = 1; File.Exists(dest); i++)
                    dest = Path.Combine(dir, name + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + i);

                try { File.Move(path, dest); }
                catch { File.Copy(path, dest); }

                Prune(dir, name);
                return dest;
            }
            catch
            {
                return null;
            }
        }

        // Keeps only the newest KeepDamagedCopies damaged copies of each file.
        static void Prune(string dir, string name)
        {
            try
            {
                var old = Directory.GetFiles(dir, name + ".damaged-*")
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .Skip(KeepDamagedCopies);
                foreach (var f in old)
                {
                    try { File.Delete(f); } catch (Exception exIgnored2) { AppLog.Write("StateStore (ignored): " + exIgnored2.Message); }
                }
            }
            catch (Exception exIgnored3) { AppLog.Write("StateStore (ignored): " + exIgnored3.Message); }
        }

        public static void Write(string file, string json)
        {
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, file, true);
            RefreshBackupIfDue(file, json);
        }

        // Inline URL credentials are removed from the active state and both automatic backups
        // during the one-time migration. Non-URL data and signed query strings stay intact.
        public static int SanitizeInlineCredentials(string file)
        {
            if (!File.Exists(file)) return 0;
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(file));
                if (root is not JsonObject state) return -1;
                int changed = 0;
                foreach (string collectionName in new[] { "Items", "Deleted" })
                {
                    if (FindProperty(state, collectionName) is not JsonArray collection) continue;
                    foreach (var node in collection)
                    {
                        if (node is not JsonObject item) continue;
                        changed += SanitizeUrl(item, "Url");
                        if (FindProperty(item, "YtDlp") is JsonObject ytdlp)
                            changed += SanitizeUrl(ytdlp, "Url");
                    }
                }
                if (changed == 0) return 0;

                string temp = file + ".sanitize-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temp, file, true);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
                return changed;
            }
            catch (Exception ex)
            {
                AppLog.Write("Could not sanitize saved URL credentials: " + ex.Message);
                return -1;
            }
        }

        static JsonNode? FindProperty(JsonObject obj, string name)
        {
            foreach (var property in obj)
                if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            return null;
        }

        static int SanitizeUrl(JsonObject obj, string name)
        {
            foreach (var property in obj.ToArray())
            {
                if (!string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase) ||
                    property.Value is not JsonValue value || !value.TryGetValue<string>(out var url) || url == null)
                    continue;
                string safe = UrlCredential.Strip(url, out _, out _);
                if (string.Equals(url, safe, StringComparison.Ordinal)) return 0;
                obj[property.Key] = safe;
                return 1;
            }
            return 0;
        }

        static void RefreshBackupIfDue(string file, string json)
        {
            try
            {
                string b1 = Backup1(file), b2 = Backup2(file);
                if (File.Exists(b1) && DateTime.UtcNow - File.GetLastWriteTimeUtc(b1) < BackupEvery) return;

                if (File.Exists(b1)) File.Copy(b1, b2, true);

                string tmp = b1 + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, b1, true);
            }
            catch (Exception exIgnored4) { /* a failed backup must never stop saving */ AppLog.Write("StateStore (ignored): " + exIgnored4.Message); }
        }

        // The file was written by a newer FastDM: keep an untouched copy once, because this older version
        // will drop any settings it does not know when it saves.
        public static string? KeepNewerCopy(string file, int version)
        {
            try
            {
                string dest = file + ".from-v" + version;
                if (File.Exists(dest) || !File.Exists(file)) return File.Exists(dest) ? dest : null;
                File.Copy(file, dest);
                return dest;
            }
            catch
            {
                return null;
            }
        }
    }
}
