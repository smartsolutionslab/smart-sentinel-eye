using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Auth;

/// <summary>
/// Validates a bearer token forwarded by MediaMTX's external auth hook
/// (FR-007). Implementation lives in Infrastructure and reuses the same
/// Keycloak configuration as the standard JWT bearer pipeline; the
/// Application layer depends only on this abstraction so it stays
/// framework-free.
/// </summary>
public interface IWhepAuthValidator
{
    Task<Option<WhepAuthSubject>> ValidateAsync(string bearerToken, CancellationToken cancellationToken);
}

/// <summary>
/// The validated principal extracted from a WHEP bearer token. <c>Scopes</c>
/// is the split form of the JWT <c>scope</c> claim so callers can check
/// for <c>sse.management</c> without parsing.
/// </summary>
public sealed record WhepAuthSubject(string Subject, IReadOnlyList<string> Scopes);
