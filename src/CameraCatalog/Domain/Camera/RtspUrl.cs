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

    private RtspUrl(string value) : base(value)
    {
    }

    public static RtspUrl From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .StartsWith(RequiredScheme, StringComparison.OrdinalIgnoreCase)
            .Satisfies(HasNoUserInfo, "must not contain a user:password@ segment")
            .AndReturn();

        return new RtspUrl(validated);
    }

    private static bool HasNoUserInfo(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }
        return string.IsNullOrEmpty(uri.UserInfo);
    }
}
