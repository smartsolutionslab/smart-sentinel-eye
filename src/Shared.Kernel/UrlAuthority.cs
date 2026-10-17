using System.Net;
using System.Net.Sockets;

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
    // The parser ends the userinfo at the first '@' and ffmpeg-style parsers at the last, and an '@' after the
    // closing ']' is moved into the path, so both readings are tried (see BracketedHostReadings). Leading
    // whitespace is skipped as the parser skips it.
    public static bool TextFollowsTheClosingBracket(string url)
    {
        Ensure.That(url).IsNotNull();

        foreach (var host in BracketedHostReadings(url))
        {
            var close = host.IndexOf(']', StringComparison.Ordinal);

            if (close >= 0 && TextAfterTheBracketIsNotAPort(host.AsSpan(close + 1)))
            {
                return true;
            }
        }

        return false;
    }

    // A '/' inside the brackets introduces a prefix length ([fe80::1/64]) and the parser reads the address before it;
    // when that text is not an IPv6 address on its own ([fe80/::1], [0/::1]) the parser reads an address nobody
    // wrote. Same readings as TextFollowsTheClosingBracket.
    public static bool SlashInsideTheBracketsFollowsNoAddress(string url)
    {
        Ensure.That(url).IsNotNull();

        foreach (var host in BracketedHostReadings(url))
        {
            var close = host.IndexOf(']', StringComparison.Ordinal);

            if (close > 0 && SlashFollowsNoAddress(host.AsSpan(1, close - 1)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SlashFollowsNoAddress(ReadOnlySpan<char> content)
    {
        if (!content.Contains('/'))
        {
            return false;
        }

        var address = content[..content.IndexOfAny('%', '/')];

        return !IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetworkV6;
    }

    // The texts a bracketed host could be read from: after the first '@' (where the parser ends the userinfo) and
    // after the last (where ffmpeg-style parsers do), or the authority's start when there is no '@'. A reading
    // with nothing (or only trailing whitespace) after its '@' falls back to the start, because the parser then keeps the bracket and moves the
    // '@' into the path (rtsp://[fe80::1]x@). Only readings that begin with '[' are kept.
    private static List<string> BracketedHostReadings(string url)
    {
        var authority = TextFromTheAuthority(url.AsSpan().TrimStart()).ToString();
        var terminator = IndexOfTheTerminator(authority);
        var userinfo = terminator < 0 ? authority : authority[..terminator];
        HashSet<int> starts = userinfo.Contains('@') ? [] : [0];

        foreach (var at in ((int[])[userinfo.IndexOf('@'), userinfo.LastIndexOf('@')]).Where(at => at >= 0))
        {
            starts.Add(userinfo[(at + 1)..].All(IsTrimmedByTheParser) ? 0 : at + 1);
        }

        return [.. starts.Where(start => start < authority.Length && authority[start] == '[').Select(start => authority[start..])];
    }

    private static bool IsTrimmedByTheParser(char character) => char.IsWhiteSpace(character) || char.IsControl(character);

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
