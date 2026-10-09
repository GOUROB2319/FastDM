namespace FastDM
{
    // Stand-ins for the two app types the linked bridge files use. The real ones live in WinForms files
    // that cannot be compiled outside Windows. Keep the member names identical to the real ones
    // (AppSettings in FastDM/Form1.cs, UpdateChecker in FastDM/UpdateChecker.cs).
    public class AppSettings
    {
        public List<string> BridgeTokens { get; set; } = new List<string>();
        public Dictionary<string, string> BridgeTokenOrigins { get; set; } = new Dictionary<string, string>();
    }

    public static class UpdateChecker
    {
        public static Version Current => new Version(1, 0, 14, 0);
        public static bool IsPackaged => false;
    }
}
