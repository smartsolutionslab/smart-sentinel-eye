namespace SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

/// <summary>
/// The shared secret ADR-0171 injects into this service to authorize
/// MediaMTX's <c>api</c> hook action and to authenticate this service's own
/// calls into MediaMTX's control API. Bound from a separate
/// <c>MediaMtxApi</c> configuration section rather than folded into
/// <see cref="MediaMtxOptions"/>, so the secret's binding is independent of
/// the plain management/WHEP URLs.
/// </summary>
public sealed class MediaMtxApiOptions
{
    public const string SectionName = "MediaMtxApi";

    public string Secret { get; set; } = string.Empty;
}
