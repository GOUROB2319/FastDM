using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace FastDM
{
    // The first connection requires explicit approval; later connections must match the saved key.
    // If the server was reinstalled, remove its entry from %LocalAppData%\FastDM\known_hosts.json.
    public static class SshHostKeys
    {
        static readonly object gate = new object();
        static Dictionary<string, string>? known;
        static bool loadFailed;
        static string? storagePathOverride;
        static Func<string, int, string, bool>? firstUseConfirmation;

        public static string FilePath => storagePathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastDM", "known_hosts.json");

        // The application supplies a UI callback. With no UI or if the callback fails, first use is denied.
        public static Func<string, int, string, bool>? FirstUseConfirmation
        {
            get => Volatile.Read(ref firstUseConfirmation);
            set => Volatile.Write(ref firstUseConfirmation, value);
        }

        public static bool Verify(string host, int port, string? keyType, byte[]? keyBytes)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length > 255 || port is < 1 or > 65535 ||
                keyBytes == null || keyBytes.Length == 0)
                return false;

            string normalizedHost = host.Trim().ToLowerInvariant();
            string id = normalizedHost + ":" + port;
            string fingerprint = (keyType ?? "") + " SHA256:" +
                Convert.ToBase64String(SHA256.HashData(keyBytes));

            lock (gate)
            {
                if (known == null)
                {
                    if (loadFailed) return false;
                    if (!TryLoad(out var loaded))
                    {
                        loadFailed = true;
                        return false;
                    }
                    known = loaded;
                    loadFailed = false;
                }

                if (known.TryGetValue(id, out var saved))
                {
                    if (string.Equals(saved, fingerprint, StringComparison.Ordinal)) return true;

                    AppLog.Write("SFTP: REFUSED " + id + " - the server key changed (saved " + saved + ", now " + fingerprint + "). " +
                                 "If the server was reinstalled, remove its line from " + FilePath + ".");
                    return false;
                }

                bool approved;
                try { approved = firstUseConfirmation?.Invoke(host, port, fingerprint) == true; }
                catch (Exception ex)
                {
                    AppLog.Write("SFTP: first-use confirmation failed: " + ex.Message);
                    return false;
                }
                if (!approved)
                {
                    AppLog.Write("SFTP: first-use key was not approved for " + id + " (" + fingerprint + ").");
                    return false;
                }

                known[id] = fingerprint;
                if (!Save())
                {
                    known.Remove(id);
                    return false;
                }

                AppLog.Write("SFTP: remembered the approved key of " + id + " (" + fingerprint + ")");
                return true;
            }
        }

        static bool TryLoad(out Dictionary<string, string> loaded)
        {
            loaded = new Dictionary<string, string>(StringComparer.Ordinal);
            string text;
            try
            {
                text = File.ReadAllText(FilePath);
            }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            catch (Exception ex)
            {
                AppLog.Write("SFTP: refusing connections because " + FilePath + " could not be read: " + ex.Message);
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
                if (parsed == null) throw new InvalidDataException("The host-key file has no JSON object.");
                foreach (var entry in parsed)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                        throw new InvalidDataException("The host-key file contains an empty entry.");
                    loaded[entry.Key] = entry.Value;
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Write("SFTP: refusing connections because " + FilePath + " could not be read: " + ex.Message);
                return false;
            }
        }

        static bool Save()
        {
            string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
                File.WriteAllText(temp, JsonSerializer.Serialize(known,
                    new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, FilePath, true);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Write("SFTP: could not save approved host key to " + FilePath + ": " + ex.Message);
                return false;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        internal static void ConfigureForTests(string path, Func<string, int, string, bool>? confirmation)
        {
            lock (gate)
            {
                storagePathOverride = path;
                firstUseConfirmation = confirmation;
                known = null;
                loadFailed = false;
            }
        }

        internal static void ResetForTests()
        {
            lock (gate)
            {
                storagePathOverride = null;
                firstUseConfirmation = null;
                known = null;
                loadFailed = false;
            }
        }
    }
}
