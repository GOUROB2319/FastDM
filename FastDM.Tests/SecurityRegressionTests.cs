using FastDM;

namespace FastDM.Tests
{
    static class SecurityRegressionTests
    {
        public static void Run()
        {
            T.Section("Credential URL and log redaction");

            string safe = UrlCredential.Strip("sftp://us%3Aer:p%40ss@example.test:2222/a.zip", out var user, out var pass);
            T.Check("URL user-info is removed and percent-decoded for the session credential", safe == "sftp://example.test:2222/a.zip" && user == "us:er" && pass == "p@ss");

            string malformed = UrlCredential.Strip("ftp://name:secret@host.test/file", out var malformedUser, out var malformedPass);
            T.Check("ordinary FTP credentials are removed", malformed == "ftp://host.test/file" && malformedUser == "name" && malformedPass == "secret");

            string encoded = UrlCredential.Strip("sftp://na%C3%AFve:p%40ss%3Aword@host.test/file", out var encodedUser, out var encodedPass);
            T.Check("encoded Unicode and delimiters survive credential extraction", encoded == "sftp://host.test/file" && encodedUser == "naïve" && encodedPass == "p@ss:word");

            string malformedUserInfo = UrlCredential.Strip("ftp://bad%ZZ:pass@host.test/file", out _, out _);
            T.Check("malformed user-info never remains in the displayed URL", !malformedUserInfo.Contains("bad%ZZ") && !malformedUserInfo.Contains("pass@"));

            string ftpQuery = UrlCredential.Strip("ftp://user:pass@host.test/file?sig=keep&part=2", out _, out _);
            T.Check("FTP query parameters remain query parameters during credential removal", ftpQuery == "ftp://host.test/file?sig=keep&part=2");

            string safeLog = AppLog.Redact("GET https://user:pass@example.test/file?token=abc&x=2 Cookie: sid=secret Authorization: Bearer token");
            T.Check("diagnostics hide URL credentials, query values, cookies, and authorization", !safeLog.Contains("user:pass") && !safeLog.Contains("token=abc") && !safeLog.Contains("sid=secret") && !safeLog.Contains("Bearer token"));
            string proxyLog = AppLog.Redact("proxy_pass=secret-value token=api-value");
            T.Check("diagnostics hide proxy passwords and standalone secret fields", !proxyLog.Contains("secret-value") && !proxyLog.Contains("api-value"));

            const string unicode = "ERROR: Sign in to confirm you’re not a bot — বাংলা ভিডিও";
            T.Check("redaction preserves Unicode diagnostics", AppLog.Redact(unicode) == unicode);
        }
    }
}
