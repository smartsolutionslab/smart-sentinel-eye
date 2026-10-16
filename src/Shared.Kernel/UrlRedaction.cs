using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SmartSentinelEye.Shared.Kernel;

public static class UrlRedaction
{
    public const string Unparseable = "<unparseable url>";

    private const string SchemeSeparator = "://";

    private const string AuthorityTerminators = "/?#\\";

    public static string ToOrigin(string url)
    {
        Ensure.That(url).IsNotNull();

        if (AtSignFollowsTheAuthority(url)
            || !TryParse(url, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !HostIsPlainAscii(uri)
            || !AuthorityWasWrittenAsAscii(url))
        {
            return Unparseable;
        }

        var origin = $"{uri.Scheme}://{uri.Host}";

        return uri.IsDefaultPort ? origin : $"{origin}:{uri.Port}";
    }

    // The parser takes an @ as the userinfo delimiter only while it precedes the first / ? # or \;
    // once the authority is cut short, text such as admin:12345 becomes the host and port and
    // would be rendered. The authority is located the way the parser does (after the scheme, past
    // any run of / and \), not only after a literal ://, which would fail open for rtsp:\\admin:1#@cam.
    // AuthorityWasWrittenAsAscii keeps the strict :// form on purpose: there an unrecognised shape is
    // refused (fail closed), here an unrecognised shape must still be scanned, so do not unify the two.
    private static bool AtSignFollowsTheAuthority(string url)
    {
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

    // Trust-boundary guard around a BCL parser fed untrusted text: Uri.TryCreate throws
    // IndexOutOfRangeException for some inputs (a file: authority holding U+200E and the like),
    // and an exception here would fail an audit write and retry-loop the message.
    private static bool TryParse(string url, [NotNullWhen(true)] out Uri? uri)
    {
        try
        {
            return Uri.TryCreate(url, UriKind.Absolute, out uri);
        }
        catch (IndexOutOfRangeException)
        {
            uri = null;

            return false;
        }
    }

    private static bool HostIsPlainAscii(Uri uri)
    {
        var isIpv6 = uri.HostNameType == UriHostNameType.IPv6;

        return uri.Host.All(character => IsHostCharacter(character, isIpv6));
    }

    private static bool IsHostCharacter(char character, bool isIpv6) =>
        char.IsAsciiLetterOrDigit(character)
        || character is '-' or '.' or '_'
        || (isIpv6 && character is '[' or ']' or ':');

    // The parser normalises some non-ASCII hosts to ASCII before Host is read, so the host
    // alone cannot prove what the caller wrote; the authority as written must be ASCII too.
    // It also accepts backslashes, or no slashes at all, as the authority marker, so a non-ASCII
    // input is only trusted in the strict scheme:// form, where the first terminator ends the authority.
    private static bool AuthorityWasWrittenAsAscii(string url)
    {
        if (Ascii.IsValid(url))
        {
            return true;
        }

        var separator = url.IndexOf(SchemeSeparator, StringComparison.Ordinal);

        if (separator <= 0 || !url[..separator].All(IsSchemeCharacter))
        {
            return false;
        }

        var authority = url.AsSpan(separator + SchemeSeparator.Length);
        var end = authority.IndexOfAny(AuthorityTerminators);

        // An empty written authority (file:////host) is refused: the parser reads the host after the extra separators.
        return end != 0 && Ascii.IsValid(end < 0 ? authority : authority[..end]);
    }

    private static bool IsSchemeCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '+' or '.' or '-';
}
