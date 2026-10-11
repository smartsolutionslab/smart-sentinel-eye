using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// The operation MediaMTX names in its external-auth hook body. <c>read</c> (a
/// WHEP viewer opening a stream), <c>publish</c> (a source pushing into a
/// path), <c>playback</c> (reading a recording) and, since ADR-0171,
/// <c>api</c> (a call against MediaMTX's own control API — config/paths and
/// runtime paths alike; MediaMTX reports the whole surface under this one
/// action).
///
/// <para>
/// <c>metrics</c> and <c>pprof</c> are deliberately absent.
/// <c>mediamtx.yml</c> still excludes both from the hook, so they never
/// arrive; modelling them would be modelling a path that does not exist.
/// Should an exclusion ever be deleted, they parse as
/// <see cref="Option{T}.None"/> and are refused rather than silently admitted.
/// </para>
/// </summary>
public sealed record MediaMtxAction(string Value) : IValueObject<string>
{
    public static MediaMtxAction Read { get; } = new("read");

    public static MediaMtxAction Publish { get; } = new("publish");

    public static MediaMtxAction Playback { get; } = new("playback");

    public static MediaMtxAction Api { get; } = new("api");

    public static MediaMtxAction From(string value) =>
        value switch
        {
            "read" => Read,
            "publish" => Publish,
            "playback" => Playback,
            "api" => Api,
            _ => throw new ArgumentException($"Unknown MediaMtxAction '{value}'.", nameof(value)),
        };

    /// <summary>
    /// Parses what arrived on the wire. Ordinal and case-sensitive: MediaMTX
    /// sends these lowercase, and a case-insensitive match would admit a value
    /// no MediaMTX build actually posts.
    /// </summary>
    public static Option<MediaMtxAction> TryFrom(string? value) =>
        value switch
        {
            "read" => Option<MediaMtxAction>.Some(Read),
            "publish" => Option<MediaMtxAction>.Some(Publish),
            "playback" => Option<MediaMtxAction>.Some(Playback),
            "api" => Option<MediaMtxAction>.Some(Api),
            _ => Option<MediaMtxAction>.None,
        };

    public sealed override string ToString() => Value;
}
