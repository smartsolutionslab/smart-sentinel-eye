namespace SmartSentinelEye.StreamDistribution.Application.Auth;

/// <summary>
/// Checks the Basic-auth password MediaMTX forwards for an <c>api</c> hook
/// call against the shared secret ADR-0171 injects into this service.
/// Implementation lives in Infrastructure, which owns the configured secret
/// and the constant-time comparison; Application depends only on this
/// abstraction so it stays framework-free, mirroring
/// <see cref="IWhepAuthValidator"/>.
/// </summary>
public interface IMediaMtxApiAuthenticator
{
    bool Authorizes(string? password);
}
