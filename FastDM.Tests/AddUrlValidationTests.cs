namespace FastDM.Tests
{
    static class AddUrlValidationTests
    {
        public static void Run()
        {
            T.Section("Add URL inline validation");

            T.Check("http, https, ftp, and sftp links are detected", AddUrlValidation.Analyze("http://a.test/x\nhttps://b.test/y\nftp://c.test/z\nsftp://d.test/q").DetectedCount == 4);
            T.Check("unsafe and malformed schemes are rejected", !AddUrlValidation.IsAllowedLink("javascript:alert(1)") && !AddUrlValidation.IsAllowedLink("not a link"));
            T.Check("blank lines are ignored", AddUrlValidation.Analyze("\n https://a.test/x \n\n").NonEmptyLineCount == 1);
            T.Check("invalid line number refers to the original input", AddUrlValidation.Analyze("https://a.test/x\n\nfile:///C:/bad").FirstInvalidLine == 3);
            T.Check("empty input reports zero detected links", AddUrlValidation.Analyze(" \r\n\t").DetectedCount == 0);
        }
    }
}
