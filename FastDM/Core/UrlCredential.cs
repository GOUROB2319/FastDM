using System;
using System.Text.RegularExpressions;

namespace FastDM
{
    // Removes URI user-info before a URL enters persisted app state.
    // Credentials are returned to the caller for session-only Credential Manager handling.
    public static class UrlCredential
    {
        static readonly Regex FallbackUserInfo = new Regex(
            @"(?i)([a-z][a-z0-9+.-]*://)[^/?#@\s]*@",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        static readonly Regex AbsoluteUserInfo = new Regex(
            @"(?i)^([a-z][a-z0-9+.-]*://)[^/?#@\s]*@",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string Strip(string? value, out string? user, out string? password)
        {
            user = null;
            password = null;
            string input = value ?? string.Empty;

            if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
            {
                string[] parts = uri.UserInfo.Split(new[] { ':' }, 2);
                try { user = Uri.UnescapeDataString(parts[0]); }
                catch (UriFormatException) { user = parts[0]; }
                try { password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty; }
                catch (UriFormatException) { password = parts.Length > 1 ? parts[1] : string.Empty; }

                string safe = AbsoluteUserInfo.Replace(input, "$1", 1);
                if (!string.Equals(safe, input, StringComparison.Ordinal)) return safe;
                var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
                return builder.Uri.AbsoluteUri;
            }

            return FallbackUserInfo.Replace(input, "$1");
        }

        public static string StripAndRemember(string? value)
        {
            string safe = Strip(value, out var user, out var password);
            if (user != null && Uri.TryCreate(safe, UriKind.Absolute, out var uri))
                CredStore.Set(uri, user, password ?? string.Empty, remember: false);
            return safe;
        }
    }
}
