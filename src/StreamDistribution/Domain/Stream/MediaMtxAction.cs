using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// The operation MediaMTX names in its external-auth hook body. Only the three
/// values that can reach us are modelled: <c>read</c> (a WHEP viewer opening a
/// stream), <c>publish</c> (a source pushing into a path) and <c>playback</c>
/// (reading a recording).
///
/// <para>
/// <c>api</c>, <c>metrics</c> and <c>pprof</c> are deliberately absent.
/// <c>mediamtx.yml:46-49</c> excludes them from the hook, so they never arrive;
/// modelling them would be modelling a path that does not exist. Should an
/// exclusion ever be deleted, they parse as <see cref="Option{T}.None"/> and are
/// refused rather than silently admitted.
/// </para>
/// </summary>
public sealed record MediaMtxAction(string Value) : IValueObject<string>
{
    public static MediaMtxAction Read { get; } = new("read");

    public static MediaMtxAction Publish { get; } = new("publish");

    public static MediaMtxAction Playback { get; } = new("playback");

    public static MediaMtxAction From(string value) =>
        value switch
        {
            "read" => Read,
            "publish" => Publish,
            "playback" => Playback,
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
            _ => Option<MediaMtxAction>.None,
        };

    public sealed override string ToString() => Value;
}
