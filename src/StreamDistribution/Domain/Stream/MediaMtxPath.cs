using System.Text.RegularExpressions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Path name MediaMTX uses internally for the camera's stream. Derived
/// deterministically from a <see cref="CameraIdentifier"/> as
/// <c>cam-{guid}</c> so the WHEP URL can be reconstructed by both backend
/// and frontend without a separate lookup.
/// </summary>
public sealed partial record MediaMtxPath : StringValueObject
{
    private static readonly Regex PathPattern = PathPatternRegex();

    private MediaMtxPath(string value) : base(value) { }

    public static MediaMtxPath For(CameraIdentifier camera) =>
        new($"cam-{camera.Value}");

    public static MediaMtxPath From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .Matches(PathPattern, "must look like cam-{guid}")
            .AndReturn();
        return new MediaMtxPath(validated);
    }

    [GeneratedRegex(
        "^cam-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PathPatternRegex();
}
