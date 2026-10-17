namespace SmartSentinelEye.Shared.Kernel;

public static class UrlAuthority
{
    internal const string AuthorityTerminators = "/?#\\";

    // The parser takes an @ as the userinfo delimiter only while it precedes the first / ? # or \;
    // once the authority is cut short, text such as admin:12345 becomes the host and port and
    // would be rendered. The skip of a run of / and \ models file: and UNC parsing, where the host follows
    // the run; for a hierarchical scheme such as rtsp: a third / ends an empty authority, so this over-reaches
    // there, and that shape is refused by the value objects' empty-host rule and rendered as the placeholder
    // by ToOrigin's empty-host check.
    // UrlRedaction.AuthorityWasWrittenAsAscii keeps the strict :// form on purpose: there an unrecognised shape is
    // refused (fail closed), here an unrecognised shape must still be scanned, so do not unify the two.
    public static bool AtSignFollowsTheAuthority(string url)
    {
        Ensure.That(url).IsNotNull();

        var authority = TextFromTheAuthority(url);
        var end = IndexOfTheTerminator(authority);

        return end >= 0 && authority[end..].Contains('@');
    }

    // The text from the authority's first character to the end of the input: past a scheme and its ':' if
    // present, then past any run of '/' and '\'. Empty when nothing follows.
    public static ReadOnlySpan<char> TextFromTheAuthority(ReadOnlySpan<char> url)
    {
        var rest = url;
        var colon = rest.IndexOf(':');

        if (colon > 0 && char.IsAsciiLetter(rest[0]) && AllSchemeCharacters(rest[..colon]))
        {
            rest = rest[(colon + 1)..];
        }

        var authorityStart = rest.IndexOfAnyExcept('/', '\\');

        return authorityStart < 0 ? [] : rest[authorityStart..];
    }

    // Index of the first '/', '?', '#' or '\' in the authority text, or -1.
    public static int IndexOfTheTerminator(ReadOnlySpan<char> authority) =>
        authority.IndexOfAny(AuthorityTerminators);

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
