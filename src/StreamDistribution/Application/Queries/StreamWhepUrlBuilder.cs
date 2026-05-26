using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Queries;

/// <summary>
/// Constructs the WHEP URL the browser POSTs its SDP offer to. The base
/// URL is configured per environment (Aspire injects the MediaMTX WHEP
/// endpoint); the path segment is derived from the stream's
/// <see cref="MediaMtxPath"/>.
/// </summary>
public interface IStreamWhepUrlBuilder
{
    string For(MediaMtxPath path);
}
