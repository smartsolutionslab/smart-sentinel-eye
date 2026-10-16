using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// RTSP URL for an IP camera. Must use the rtsp:// scheme, must be 1–2048
/// characters, and must not contain a userinfo segment (user:password@) per
/// spec 001-register-camera FR-008 — credentialed cameras are deferred.
/// </summary>
public sealed record RtspUrl : StringValueObject
{
    public const int MaximumLength = 2048;
    private const string RequiredScheme = "rtsp://";

    private const string AtSignAfterAuthorityMessage =
        "must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. "
        + "Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); "
        + "a user:password@ segment is not accepted either";

    private RtspUrl(string value) : base(value)
    {
    }

    public static RtspUrl From(string value)
    {
        EnsureStoredInvariants(value)
            .Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), AtSignAfterAuthorityMessage);
        return new RtspUrl(value);
    }

    // Persistence read only: a row written before the raw-@ rule stays readable. Every write goes through From.
    public static RtspUrl FromStored(string value)
    {
        EnsureStoredInvariants(value);
        return new RtspUrl(value);
    }

    private static EnsuredString EnsureStoredInvariants(string value) =>
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .StartsWith(RequiredScheme, StringComparison.OrdinalIgnoreCase)
            .Satisfies(HasNoUserInfo, "must not contain a user:password@ segment");

    private static bool HasNoUserInfo(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }
        return string.IsNullOrEmpty(uri.UserInfo);
    }
}
