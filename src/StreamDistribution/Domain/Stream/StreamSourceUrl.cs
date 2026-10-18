using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// The RTSP source MediaMTX pulls for a stream. Mirrors the invariants of
/// <c>CameraCatalog.Domain.Camera.RtspUrl</c> but is declared here so the value
/// crosses the context boundary as a primitive and is re-validated on the way
/// in, rather than by referencing the other context's type.
///
/// <para>
/// Persisted on the aggregate so <c>MediaMtxReconciler</c> can re-create a
/// missing MediaMTX path on startup. Without it the reconciler knows a path's
/// <em>name</em> (<c>cam-{guid}</c>) but not what to point it at, so a MediaMTX
/// restart left every stream 404ing on WHEP open until a CameraRegistered
/// redelivery happened to re-provision it.
/// </para>
///
/// <para>
/// An empty host and text after the closing ']' of an IPv6 host are refused on write, because the parser
/// reads a different host than the one written (spec 392). So are an '@', '?' or '#' inside the brackets and a
/// host the parser cannot read (spec 394). A control character or white space anywhere is refused on write (spec 408).
/// A zone holding anything but printable ASCII, and a host of only '.', '-' or '_', are refused on write (spec 411), and so is a ':' after the host with no port (spec 414).
/// </para>
/// </summary>
public sealed record StreamSourceUrl : StringValueObject
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

    private StreamSourceUrl(string value) : base(value)
    {
    }

    public static StreamSourceUrl From(string value)
    {
        EnsureStoredInvariants(value)
            .Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), AtSignAfterAuthorityMessage)
            .Satisfies(url => UrlAuthority.TryReadHost(url, out _), UnreadableHostMessage)
            .Satisfies(HasHost, EmptyHostMessage)
            .Satisfies(url => !UrlAuthority.TextFollowsTheClosingBracket(url), TextAfterBracketMessage)
            .Satisfies(url => !UrlAuthority.SlashInsideTheBracketsFollowsNoAddress(url), SlashInBracketsMessage)
            .Satisfies(url => !UrlAuthority.DelimiterInsideTheBrackets(url), DelimiterInsideBracketsMessage)
            .Satisfies(url => !UrlAuthority.ContainsControlOrWhiteSpace(url), ControlOrWhiteSpaceMessage)
            .Satisfies(url => !UrlAuthority.NonPrintableCharacterInsideTheZone(url), NonPrintableZoneMessage)
            .Satisfies(HostIsNotPunctuationOnly, PunctuationOnlyHostMessage)
            .Satisfies(url => !UrlAuthority.WrittenPortIsEmpty(url), EmptyPortMessage);
        return new StreamSourceUrl(value);
    }

    // Persistence read only: a row written before the raw-@ rule stays readable. Every write goes through From.
    public static StreamSourceUrl FromStored(string value)
    {
        EnsureStoredInvariants(value);
        return new StreamSourceUrl(value);
    }

    private static EnsuredString EnsureStoredInvariants(string value) =>
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .StartsWith(RequiredScheme, StringComparison.OrdinalIgnoreCase)
            .Satisfies(HasNoUserInfo, "must not contain a user:password@ segment");

    private static bool HasHost(string url) =>
        UrlAuthority.TryReadHost(url, out var host) && !UrlAuthority.IsBlankHost(host);

    private static bool HostIsNotPunctuationOnly(string url) =>
        UrlAuthority.TryReadHost(url, out var host) && !UrlAuthority.IsPunctuationOnlyHost(host);

    private static bool HasNoUserInfo(string url) =>
        UrlAuthority.TryReadUserInfo(url, out var userInfo) && userInfo.Length == 0;
}
