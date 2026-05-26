using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

/// <summary>
/// Constructs the WHEP URL the browser POSTs its SDP offer to. The base
/// URL is the Aspire-injected MediaMTX WHEP endpoint
/// (<c>services:mediamtx:whep:0</c>); the path segment is the stream's
/// <see cref="MediaMtxPath"/>.
/// </summary>
public sealed class MediaMtxWhepUrlBuilder(IOptions<MediaMtxOptions> options) : IStreamWhepUrlBuilder
{
    public string For(MediaMtxPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string baseUrl = options.Value.WhepBaseUrl.TrimEnd('/');
        return $"{baseUrl}/{path.Value}/whep";
    }
}
