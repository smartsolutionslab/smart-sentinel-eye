using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Whether MediaMTX is repacketizing the RTSP source directly (Passthrough,
/// H.264 sources) or transcoding via FFmpeg (Software, for H.265/MJPEG)
/// per spec 002 FR-003. <c>Unknown</c> is the initial value before the first
/// successful frame negotiation.
/// </summary>
public sealed record TranscodeMode(string Value) : IValueObject<string>
{
    public static TranscodeMode Passthrough { get; } = new("Passthrough");

    public static TranscodeMode Software { get; } = new("Software");

    public static TranscodeMode Unknown { get; } = new("Unknown");

    public static TranscodeMode From(string value) =>
        value switch
        {
            "Passthrough" => Passthrough,
            "Software" => Software,
            "Unknown" => Unknown,
            _ => throw new ArgumentException($"Unknown TranscodeMode '{value}'.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
