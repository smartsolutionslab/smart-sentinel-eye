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
/// </summary>
public sealed record StreamSourceUrl : StringValueObject
{
    public const int MaximumLength = 2048;
    private const string RequiredScheme = "rtsp://";

    private StreamSourceUrl(string value) : base(value)
    {
    }

    public static StreamSourceUrl From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .StartsWith(RequiredScheme, StringComparison.OrdinalIgnoreCase)
            .Satisfies(HasNoUserInfo, "must not contain a user:password@ segment");
        return new StreamSourceUrl(value);
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
