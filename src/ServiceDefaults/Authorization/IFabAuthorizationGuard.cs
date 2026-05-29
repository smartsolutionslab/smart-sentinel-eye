using System.Security.Claims;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Central guard for fab-scoped endpoints (spec 008 FR-019).
/// Every endpoint that accepts a <c>fabId</c> (query or path)
/// calls <see cref="EnsureAccessAsync"/> right after model
/// binding; the guard verifies the caller's JWT
/// <c>groups</c> claim contains <c>/fabs/&lt;fabId&gt;</c>.
/// </summary>
public interface IFabAuthorizationGuard
{
    /// <summary>
    /// Throws <see cref="FabAuthorizationException"/> (mapped to
    /// 403 globally) when the caller's <c>groups</c> claim does
    /// not include <c>/fabs/&lt;fabId&gt;</c>. Returns successfully
    /// otherwise.
    /// </summary>
    Task EnsureAccessAsync(ClaimsPrincipal user, string fabId, CancellationToken cancellationToken);
}

public sealed class DefaultFabAuthorizationGuard : IFabAuthorizationGuard
{
    public const string GroupClaimType = "groups";
    public const string FabGroupPrefix = "/fabs/";

    public Task EnsureAccessAsync(ClaimsPrincipal user, string fabId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(fabId);
        cancellationToken.ThrowIfCancellationRequested();

        string targetGroup = FabGroupPrefix + fabId;
        foreach (Claim claim in user.FindAll(GroupClaimType))
        {
            // Keycloak emits group memberships as either repeated
            // single-value claims or one space-separated claim; split
            // defensively.
            string[] tokens = claim.Value.Split(
                [' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Contains(targetGroup, StringComparer.Ordinal))
            {
                return Task.CompletedTask;
            }
        }
        throw new FabAuthorizationException(fabId);
    }
}

/// <summary>
/// Thrown by <see cref="IFabAuthorizationGuard.EnsureAccessAsync"/>
/// when the caller is not a member of the requested fab. Mapped
/// to a 403 with <c>title = RESOURCE_FAB_NOT_AUTHORIZED</c>
/// globally.
/// </summary>
public sealed class FabAuthorizationException : Exception
{
    public string FabId { get; }

    public FabAuthorizationException(string fabId)
        : base($"Caller is not authorized to access fab '{fabId}'.")
    {
        FabId = fabId;
    }
}
