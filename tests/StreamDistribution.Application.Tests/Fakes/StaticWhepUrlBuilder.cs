using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

public sealed class StaticWhepUrlBuilder(string baseUrl = "http://mediamtx.test:8889") : IStreamWhepUrlBuilder
{
    public string For(MediaMtxPath path) => $"{baseUrl}/{path.Value}/whep";
}
