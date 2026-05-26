namespace SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

/// <summary>
/// MediaMTX endpoint configuration. Aspire injects the URLs as
/// <c>services:mediamtx:api:0</c> + <c>services:mediamtx:whep:0</c>; the
/// <see cref="MediaMtxOptions"/> binding in
/// <c>StreamDistributionInfrastructureModule</c> falls back through the
/// connection-string lookup.
/// </summary>
public sealed class MediaMtxOptions
{
    public const string SectionName = "MediaMtx";

    public string ManagementUrl { get; set; } = string.Empty;

    public string WhepBaseUrl { get; set; } = string.Empty;
}
