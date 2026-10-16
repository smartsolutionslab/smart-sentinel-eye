namespace SmartSentinelEye.Shared.Kernel;

public static class UrlAuthority
{
    internal const string AuthorityTerminators = "/?#\\";

    // The parser takes an @ as the userinfo delimiter only while it precedes the first / ? # or \;
    // once the authority is cut short, text such as admin:12345 becomes the host and port and
    // would be rendered. The authority is located the way the parser does (after the scheme, past
    // any run of / and \), not only after a literal ://, which would fail open for rtsp:\\admin:1#@cam.
    // UrlRedaction.AuthorityWasWrittenAsAscii keeps the strict :// form on purpose: there an unrecognised shape is
    // refused (fail closed), here an unrecognised shape must still be scanned, so do not unify the two.
    public static bool AtSignFollowsTheAuthority(string url)
    {
        Ensure.That(url).IsNotNull();

        var rest = url.AsSpan();
        var colon = rest.IndexOf(':');

        if (colon > 0 && char.IsAsciiLetter(rest[0]) && AllSchemeCharacters(rest[..colon]))
        {
            rest = rest[(colon + 1)..];
        }

        var authorityStart = rest.IndexOfAnyExcept('/', '\\');

        if (authorityStart < 0)
        {
            return false;
        }

        var authority = rest[authorityStart..];
        var end = authority.IndexOfAny(AuthorityTerminators);

        return end >= 0 && authority[end..].Contains('@');
    }

    internal static bool IsSchemeCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '+' or '.' or '-';

    private static bool AllSchemeCharacters(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (!IsSchemeCharacter(character))
            {
                return false;
            }
        }

        return true;
    }
}
