using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using HtmlAgilityPack;
using Renci.SshNet;
using System.Diagnostics;

namespace FastDM
{
    // ====================== সাধারণ টাইপ ======================
    public class AuthRequiredException : Exception
    {
        public AuthRequiredException() : base("Authentication required (username/password needed)") { }
    }

    // ====================== ক্রেডেনশিয়াল স্টোর ======================
    // সেশন মেমোরি + (Remember দিলে) Windows Credential Manager
    public static class CredStore
    {
        static readonly Dictionary<string, KeyValuePair<string, string>> session =
            new Dictionary<string, KeyValuePair<string, string>>();

        public static string KeyFor(Uri u) =>
            (u.Scheme + "://" + u.Host + ":" + u.Port).ToLowerInvariant();

        public static bool TryGet(Uri u, out string user, out string pass)
        {
            user = string.Empty; pass = string.Empty;

            // ইউআরএলের ভেতরেই থাকলে (ftp://user:pass@host/)
            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                var parts = u.UserInfo.Split(new[] { ':' }, 2);
                user = Uri.UnescapeDataString(parts[0]);
                pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                return true;
            }

            string key = KeyFor(u);
            lock (session)
            {
                if (session.TryGetValue(key, out var kv)) { user = kv.Key; pass = kv.Value; return true; }
            }
            return ReadCred("FastDM:" + key, out user, out pass);
        }

        public static void Set(Uri u, string user, string pass, bool remember)
        {
            string key = KeyFor(u);
            lock (session) session[key] = new KeyValuePair<string, string>(user, pass);
            if (remember) WriteCred("FastDM:" + key, user, pass);
        }

        // ---- Win32 Credential Manager ----
        const uint CRED_TYPE_GENERIC = 1;
        const uint CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
        static extern bool CredWrite(ref CREDENTIAL cred, uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
        static extern bool CredRead(string target, uint type, uint flags, out IntPtr cred);

        [DllImport("advapi32.dll")]
        static extern void CredFree(IntPtr buffer);

        static void WriteCred(string target, string user, string pass)
        {
            byte[] blob = Encoding.Unicode.GetBytes(pass ?? "");
            IntPtr mem = Marshal.AllocHGlobal(Math.Max(1, blob.Length));
            try
            {
                if (blob.Length > 0) Marshal.Copy(blob, 0, mem, blob.Length);
                var c = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    UserName = user,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = mem,
                    Persist = CRED_PERSIST_LOCAL_MACHINE
                };
                CredWrite(ref c, 0);
            }
            catch { }
            finally { Marshal.FreeHGlobal(mem); }
        }

        static bool ReadCred(string target, out string user, out string pass)
        {
            user = string.Empty; pass = string.Empty;
            try
            {
                if (!CredRead(target, CRED_TYPE_GENERIC, 0, out IntPtr p)) return false;
                try
                {
                    var c = Marshal.PtrToStructure<CREDENTIAL>(p);
                    user = c.UserName;
                    pass = c.CredentialBlobSize > 0
                        ? Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2)
                        : "";
                    return true;
                }
                finally { CredFree(p); }
            }
            catch { return false; }
        }
    }

    // ====================== Windows Credential Manager (সাধারণ সিক্রেট) ======================
    static class SecretStore
    {
        const uint TYPE_GENERIC = 1;
        const uint PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CRED
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
        static extern bool CredWrite(ref CRED cred, uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
        static extern bool CredRead(string target, uint type, uint flags, out IntPtr cred);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
        static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        static extern void CredFree(IntPtr buffer);

        static string Target(string key) => "FastDM:secret:" + key;

        public static void Write(string key, string user, string pass)
        {
            byte[] blob = Encoding.Unicode.GetBytes(pass ?? "");
            IntPtr mem = Marshal.AllocHGlobal(Math.Max(1, blob.Length));
            try
            {
                if (blob.Length > 0) Marshal.Copy(blob, 0, mem, blob.Length);
                var c = new CRED
                {
                    Type = TYPE_GENERIC,
                    TargetName = Target(key),
                    UserName = user,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = mem,
                    Persist = PERSIST_LOCAL_MACHINE
                };
                CredWrite(ref c, 0);
            }
            catch { }
            finally { Marshal.FreeHGlobal(mem); }
        }

        public static bool Read(string key, out string? user, out string? pass)
        {
            user = null; pass = null;
            try
            {
                if (!CredRead(Target(key), TYPE_GENERIC, 0, out IntPtr p)) return false;
                try
                {
                    var c = Marshal.PtrToStructure<CRED>(p);
                    user = c.UserName;
                    pass = c.CredentialBlobSize > 0
                        ? Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2)
                        : "";
                    return true;
                }
                finally { CredFree(p); }
            }
            catch { return false; }
        }

        public static void Delete(string key)
        {
            try { CredDelete(Target(key), TYPE_GENERIC, 0); } catch { }
        }
    }
}
