using FastDM;

namespace FastDM.Tests
{
    static class SshHostKeyTests
    {
        public static void Run()
        {
            T.Section("SFTP host-key trust");
            string root = Path.Combine(Path.GetTempPath(), "FastDM-hostkeys-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "known_hosts.json");
            byte[] firstKey = { 1, 2, 3, 4 };
            byte[] changedKey = { 4, 3, 2, 1 };

            try
            {
                SshHostKeys.ConfigureForTests(path, null);
                T.Check("first-use keys are denied without a confirmation UI", !SshHostKeys.Verify("sftp.example", 22, "ssh-ed25519", firstKey) && !File.Exists(path));

                int prompts = 0;
                SshHostKeys.ConfigureForTests(path, (_, _, _) => { prompts++; return false; });
                T.Check("declining first-use trust does not persist the key", !SshHostKeys.Verify("sftp.example", 22, "ssh-ed25519", firstKey) && !File.Exists(path) && prompts == 1);

                SshHostKeys.ConfigureForTests(path, (_, _, _) => { prompts++; return true; });
                bool approved = SshHostKeys.Verify("sftp.example", 22, "ssh-ed25519", firstKey);
                bool remembered = SshHostKeys.Verify("sftp.example", 22, "ssh-ed25519", firstKey);
                T.Check("approved fingerprints persist and subsequent matching connections need no prompt", approved && remembered && prompts == 2 && File.Exists(path));

                T.Check("a changed host key is refused without another trust prompt", !SshHostKeys.Verify("sftp.example", 22, "ssh-ed25519", changedKey) && prompts == 2);

                File.WriteAllText(path, "not valid json");
                SshHostKeys.ConfigureForTests(path, (_, _, _) => { prompts++; return true; });
                T.Check("a corrupt host-key file fails closed instead of silently relearning a key", !SshHostKeys.Verify("new.example", 22, "ssh-ed25519", firstKey) && prompts == 2);

                string blockedParent = Path.Combine(root, "file-is-not-a-directory");
                File.WriteAllText(blockedParent, "block");
                string blockedPath = Path.Combine(blockedParent, "known_hosts.json");
                SshHostKeys.ConfigureForTests(blockedPath, (_, _, _) => true);
                T.Check("an approved key is still refused when its fingerprint cannot be saved", !SshHostKeys.Verify("save-fail.example", 22, "ssh-ed25519", firstKey));
            }
            finally
            {
                SshHostKeys.ResetForTests();
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
