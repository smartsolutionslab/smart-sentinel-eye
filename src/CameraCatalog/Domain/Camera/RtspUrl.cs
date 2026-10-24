using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// RTSP URL for an IP camera. Must use the rtsp:// scheme, must be 1–2048
/// characters, and must not contain a userinfo segment (user:password@) on write per
/// spec 001-register-camera FR-008 — credentialed cameras are deferred; a stored row holding one is still read. A raw
/// @ after the authority (in the path, query or fragment) is also refused on
/// write, because a parser can misread it as userinfo (spec 389).
/// An empty host and text after the closing ']' of an IPv6 host are refused on write too, because the
/// parser reads a different host than the one written (spec 392). So are an '@', '?' or '#' inside the brackets and
/// a host the parser cannot read (spec 394). A control character or white space anywhere is refused on write (spec 408).
/// A zone holding anything but printable ASCII, and a host of only '.', '-' or '_', are refused on write (spec 411), and so is a ':' after the host with no port (spec 414).
/// A plain host holding any non-ASCII character is refused on write (spec 421).
/// </summary>
public sealed record RtspUrl : StringValueObject
{
    public const int MaximumLength = 2048;
    private const string RequiredScheme = "rtsp://";

    private const string AtSignAfterAuthorityMessage =
        "must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. "
        + "Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); "
        + "a user:password@ segment is not accepted either";

    private const string EmptyHostMessage =
        "must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path";

    private const string SlashInBracketsMessage =
        "must not have a '/' inside the brackets of an IPv6 address unless it follows a whole IPv6 address and starts a prefix length: the parser reads an address that was not written";

    private const string UnreadableHostMessage =
        "must name a host the parser can read: the address inside the brackets is malformed, for example a ':' left before a '/' prefix length";

    private const string DelimiterInsideBracketsMessage =
        "must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host";

    private const string ControlOrWhiteSpaceMessage =
        "must not contain a control character or whitespace anywhere: a line break, tab or space is copied into the request a reader sends. "
        + "Percent-encode a space in a path or query as %20";

    private const string TextAfterBracketMessage =
        "must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. "
        + "Write a zone inside the brackets, percent-encoded as %25";

    private const string NonPrintableZoneMessage =
        "must hold only printable ASCII in the zone of an IPv6 address (after the '%'), written or percent-encoded: a control character, space or invisible character there is decoded by some readers and not by others";

    private const string PunctuationOnlyHostMessage =
        "must name a host holding a letter or digit: a host of only '.', '-' or '_' names no machine";

    private const string EmptyPortMessage =
        "must write a port after the ':' that follows the host, or leave the ':' out: an empty port is read as the default port by some readers and as port 0 by others";

    private const string NonAsciiHostMessage =
        "must write the host in ASCII: a non-ASCII or invisible character there is shown as written but read differently, or dropped, by the parser. "
        + "Write an internationalised name in its xn-- form";

    private RtspUrl(string value) : base(value)
    {
    }

    public static RtspUrl From(string value)
    {
        EnsureStoredInvariants(value)
            .Satisfies(HasNoUserInfo, "must not contain a user:password@ segment")
            .Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), AtSignAfterAuthorityMessage)
            .Satisfies(url => UrlAuthority.TryReadHost(url, out _), UnreadableHostMessage)
            .Satisfies(HasHost, EmptyHostMessage)
            .Satisfies(url => !UrlAuthority.TextFollowsTheClosingBracket(url), TextAfterBracketMessage)
            .Satisfies(url => !UrlAuthority.SlashInsideTheBracketsFollowsNoAddress(url), SlashInBracketsMessage)
            .Satisfies(url => !UrlAuthority.DelimiterInsideTheBrackets(url), DelimiterInsideBracketsMessage)
            .Satisfies(url => !UrlAuthority.ContainsControlOrWhiteSpace(url), ControlOrWhiteSpaceMessage)
            .Satisfies(url => !UrlAuthority.NonPrintableCharacterInsideTheZone(url), NonPrintableZoneMessage)
            .Satisfies(HostIsNotPunctuationOnly, PunctuationOnlyHostMessage)
            .Satisfies(url => !UrlAuthority.WrittenPortIsEmpty(url), EmptyPortMessage)
            .Satisfies(url => !UrlAuthority.NonAsciiCharacterInsideThePlainHost(url), NonAsciiHostMessage);
        return new RtspUrl(value);
    }

    // Persistence read only: a row written before the raw-@ rule, or holding a user:password@ segment, stays readable.
    // The credential is masked at the read boundary (UrlRedaction.ToOrigin), not refused here. Every write goes through From.
    public static RtspUrl FromStored(string value)
    {
        EnsureStoredInvariants(value);
        return new RtspUrl(value);
    }

    private static EnsuredString EnsureStoredInvariants(string value) =>
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .StartsWith(RequiredScheme, StringComparison.OrdinalIgnoreCase);

    private static bool HasHost(string url) =>
        UrlAuthority.TryReadHost(url, out var host) && !UrlAuthority.IsBlankHost(host);

    private static bool HostIsNotPunctuationOnly(string url) =>
        UrlAuthority.TryReadHost(url, out var host) && !UrlAuthority.IsPunctuationOnlyHost(host);

    private static bool HasNoUserInfo(string url) =>
        UrlAuthority.TryReadUserInfo(url, out var userInfo) && userInfo.Length == 0;
}
