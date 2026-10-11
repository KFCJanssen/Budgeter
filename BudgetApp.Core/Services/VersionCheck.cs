using System;

namespace BudgetApp.Services
{
    /// <summary>Compares release tags like "v1.3" with the running version.</summary>
    public static class VersionCheck
    {
        public static bool TryParseTag(string? tag, out Version version)
        {
            version = new Version(0, 0);
            var s = (tag ?? "").Trim().TrimStart('v', 'V');
            int dash = s.IndexOf('-');
            if (dash > 0) s = s[..dash];
            if (!Version.TryParse(s.Contains('.') ? s : s + ".0", out var v) || v is null) return false;
            version = Normalize(v);
            return true;
        }

        public static bool IsNewer(string? tag, Version current) =>
            TryParseTag(tag, out var latest) && latest > Normalize(current);

        /// <summary>1.2 == 1.2.0 == 1.2.0.0</summary>
        public static Version Normalize(Version v) =>
            new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

        public static string Display(Version v) =>
            v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
    }
}
