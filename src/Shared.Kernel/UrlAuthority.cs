using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace SmartSentinelEye.Shared.Kernel;

public static class UrlAuthority
{
    internal const string AuthorityTerminators = "/?#\\";

    private const string BracketDelimiters = "@?#";

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

    // Uri reads an empty port and no port alike (Port -1, the ':' dropped from Authority), while ffmpeg reads port 0,
    // so only the written text shows that the ':' names no port. Bracket-aware: the ':' counts only when it directly
    // follows the closing ']' (a ':' inside the brackets is part of the address). Works on spans, so it cannot throw.
    // It relies on the userinfo and whitespace rules having run first: 'rtsp://a@[::1]@h:' returns false here and is
    // refused upstream, as is a ':' followed only by whitespace.
    public static bool WrittenPortIsEmpty(string url)
    {
        Ensure.That(url).IsNotNull();

        var readings = BracketedHostReadings(url);

        if (readings.Count > 0)
        {
            return readings.Any(HasAnEmptyPortAfterTheClosingBracket);
        }

        var authority = TextFromTheAuthority(url.AsSpan().TrimStart());
        var end = IndexOfTheTerminator(authority);
        var userinfoAndHost = end < 0 ? authority : authority[..end];
        var host = userinfoAndHost[(userinfoAndHost.LastIndexOf('@') + 1)..];

        return !host.IsEmpty && host[0] != '[' && host[^1] == ':';
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

    // An '@', '?' or '#' between the brackets is never part of an IPv6 address or a zone (RFC 6874), and a parser
    // that splits the authority at the first '?' or '#', or ends the userinfo at the last '@', reads another host.
    // Same readings as TextFollowsTheClosingBracket; works on spans only, so it cannot throw.
    public static bool DelimiterInsideTheBrackets(string url)
    {
        Ensure.That(url).IsNotNull();

        foreach (var host in BracketedHostReadings(url))
        {
            var close = host.IndexOf(']', StringComparison.Ordinal);

            if (close > 0 && host.AsSpan(1, close - 1).IndexOfAny(BracketDelimiters) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // RFC 6874 makes a zone ASCII (unreserved or percent-encoded). A reader that decodes it (Go net/url) and one that
    // does not (Uri) see different interface names, and a decoded control character is a line break in the address.
    // Works on spans only, so it cannot throw.
    public static bool NonPrintableCharacterInsideTheZone(string url)
    {
        Ensure.That(url).IsNotNull();

        foreach (var host in BracketedHostReadings(url))
        {
            var close = host.IndexOf(']', StringComparison.Ordinal);

            if (close > 0 && ZoneHasNonPrintable(ZoneOf(host.AsSpan(1, close - 1))))
            {
                return true;
            }
        }

        return false;
    }

    // The parser throws IndexOutOfRangeException from Host for a ':' left before a '/' prefix length inside the
    // brackets ([1:2:3:4:5:6:7:8:/64]) and from UserInfo for other malformed brackets; the two reads fail on
    // different inputs, so they are separate members.
    public static bool TryReadHost(string url, [NotNullWhen(true)] out string? host)
    {
        Ensure.That(url).IsNotNull();

        try
        {
            host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
        }
        catch (IndexOutOfRangeException)
        {
            host = null;
        }

        return host is not null;
    }

    public static bool TryReadUserInfo(string url, [NotNullWhen(true)] out string? userInfo)
    {
        Ensure.That(url).IsNotNull();

        try
        {
            userInfo = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.UserInfo : null;
        }
        catch (IndexOutOfRangeException)
        {
            userInfo = null;
        }

        return userInfo is not null;
    }

    // True when the host holds nothing a person could read: empty, or only whitespace, control and format
    // characters (U+00A0, U+200B, U+3000, U+FEFF and the like), which the parser keeps as a host. A plain DNS
    // host with such a character among others is not caught.
    public static bool IsBlankHost(string host)
    {
        Ensure.That(host).IsNotNull();

        return host.All(character =>
            char.IsWhiteSpace(character)
            || char.IsControl(character)
            || char.GetUnicodeCategory(character) == UnicodeCategory.Format);
    }

    // A host of only '.', '-' or '_' names no machine. Deliberately not "holds a letter or digit", which would refuse
    // internationalised hosts.
    public static bool IsPunctuationOnlyHost(string host)
    {
        Ensure.That(host).IsNotNull();

        return host.Length > 0 && host.All(character => character is '.' or '-' or '_');
    }

    // A line break, tab or space inside an address is copied into the request line a reader sends, so one written
    // into a camera URL can inject a header. char.IsControl or char.IsWhiteSpace, and deliberately not IsBlankHost's
    // wider set: format characters such as U+200B stay accepted.
    public static bool ContainsControlOrWhiteSpace(string url)
    {
        Ensure.That(url).IsNotNull();

        return url.Any(IsTrimmedByTheParser);
    }

    // A run at either end is trimmed by the parser (SP/TAB/CR/LF) or never reaches the host ToOrigin renders, so only
    // a member with a non-member on both sides changes what is rendered.
    public static bool ContainsControlOrWhiteSpaceWithinTheText(string url)
    {
        Ensure.That(url).IsNotNull();

        var text = url.AsSpan();

        while (!text.IsEmpty && IsTrimmedByTheParser(text[0]))
        {
            text = text[1..];
        }

        while (!text.IsEmpty && IsTrimmedByTheParser(text[^1]))
        {
            text = text[..^1];
        }

        foreach (var character in text)
        {
            if (IsTrimmedByTheParser(character))
            {
                return true;
            }
        }

        return false;
    }

    // The text after the first '%' of the bracket content, less one leading "25": both %25 and a bare % delimit a zone.
    private static ReadOnlySpan<char> ZoneOf(ReadOnlySpan<char> content)
    {
        var percent = content.IndexOf('%');

        if (percent < 0)
        {
            return [];
        }

        var zone = content[(percent + 1)..];

        return zone.StartsWith("25") ? zone[2..] : zone;
    }

    // A '%' followed by two hex digits is one octet; every other code unit, a malformed '%' included, is itself.
    private static bool ZoneHasNonPrintable(ReadOnlySpan<char> zone)
    {
        var index = 0;

        while (index < zone.Length)
        {
            var isOctet = zone[index] == '%' && index + 2 < zone.Length && char.IsAsciiHexDigit(zone[index + 1]) && char.IsAsciiHexDigit(zone[index + 2]);
            var value = isOctet ? int.Parse(zone.Slice(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) : zone[index];

            if (value is < 0x21 or > 0x7E)
            {
                return true;
            }

            index += isOctet ? 3 : 1;
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

    private static bool HasAnEmptyPortAfterTheClosingBracket(string host)
    {
        var close = host.IndexOf(']', StringComparison.Ordinal);

        if (close < 0)
        {
            return false;
        }

        var rest = host.AsSpan(close + 1);
        var end = IndexOfTheTerminator(rest);

        return (end < 0 ? rest : rest[..end]).SequenceEqual(":");
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
