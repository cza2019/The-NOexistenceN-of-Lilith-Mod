using System;

namespace LilithMod;

internal static class UpdateReleaseUrl
{
    private const string TrustedHost = "github.com";
    private const string TrustedTagPrefix =
        "/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/tag/";

    internal static Uri? Select(bool accepted, string value)
    {
        return accepted && TryParse(value, out Uri? release) ? release : null;
    }

    internal static bool TryParse(string value, out Uri? release)
    {
        release = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate)
            || !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.IdnHost, TrustedHost, StringComparison.OrdinalIgnoreCase)
            || !candidate.IsDefaultPort
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !candidate.AbsolutePath.StartsWith(TrustedTagPrefix, StringComparison.OrdinalIgnoreCase)
            || candidate.AbsolutePath.Length <= TrustedTagPrefix.Length)
        {
            return false;
        }
        release = candidate;
        return true;
    }
}
