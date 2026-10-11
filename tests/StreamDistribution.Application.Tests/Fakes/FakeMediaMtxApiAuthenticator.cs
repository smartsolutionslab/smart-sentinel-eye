using SmartSentinelEye.StreamDistribution.Application.Auth;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IMediaMtxApiAuthenticator"/> for handler tests.
/// Configure <see cref="Result"/> to decide whether a password authorizes,
/// without exercising the real comparison.
/// </summary>
public sealed class FakeMediaMtxApiAuthenticator : IMediaMtxApiAuthenticator
{
    public bool Result { get; set; }

    public bool Authorizes(string? password) => Result;
}
