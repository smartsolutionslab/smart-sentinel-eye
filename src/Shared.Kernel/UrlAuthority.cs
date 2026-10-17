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

    // Text written after a bracketed host's closing ']' other than ':' and digits (a port) is dropped by the parser
    // from the host, so the written authority is longer than the host it read (a zone written as %25eth0 outside the
    // brackets, or a hostname glued on). Works on the written text with no parse, so it cannot throw.
    public static bool TextFollowsTheClosingBracket(string url)
    {
        Ensure.That(url).IsNotNull();

        var authority = TextFromTheAuthority(url);
        var terminator = IndexOfTheTerminator(authority);
        var userinfoEnd = (terminator < 0 ? authority : authority[..terminator]).LastIndexOf('@');
        var host = authority[(userinfoEnd + 1)..];

        if (host.IsEmpty || host[0] != '[')
        {
            return false;
        }

        var close = host.IndexOf(']');

        return close >= 0 && TextAfterTheBracketIsNotAPort(host[(close + 1)..]);
    }

    private static bool TextAfterTheBracketIsNotAPort(ReadOnlySpan<char> rest)
    {
        if (rest.IsEmpty || IndexOfTheTerminator(rest) == 0)
        {
            return false;
        }

        if (rest[0] != ':')
        {
            return true;
        }

        var port = rest[1..];
        var end = IndexOfTheTerminator(port);

        return (end < 0 ? port : port[..end]).ContainsAnyExceptInRange('0', '9');
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
