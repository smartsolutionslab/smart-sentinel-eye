namespace SmartSentinelEye.Shared.Kernel;

public static class UrlRedaction
{
    public const string Unparseable = "<unparseable url>";

    private const string AuthorityMarker = "//";

    private const string AuthorityTerminators = "/?#\\";

    public static string ToOrigin(string url)
    {
        Ensure.That(url).IsNotNull();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !HostIsPlainAscii(uri)
            || !AuthorityWasWrittenAsAscii(url))
        {
            return Unparseable;
        }

        var origin = $"{uri.Scheme}://{uri.Host}";

        return uri.IsDefaultPort ? origin : $"{origin}:{uri.Port}";
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
    private static bool AuthorityWasWrittenAsAscii(string url)
    {
        if (url.All(char.IsAscii))
        {
            return true;
        }

        var start = url.IndexOf(AuthorityMarker, StringComparison.Ordinal);

        if (start < 0)
        {
            return false;
        }

        var authority = url.AsSpan(start + AuthorityMarker.Length);
        var end = authority.IndexOfAny(AuthorityTerminators);

        return (end < 0 ? authority : authority[..end]).ToString().All(char.IsAscii);
    }
}
