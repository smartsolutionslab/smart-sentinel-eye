using System.Security.Claims;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Central guard for fab-scoped endpoints (spec 008 FR-019).
/// Every endpoint that accepts a <c>fabId</c> (query or path)
/// calls <see cref="EnsureAccessAsync"/> right after model
/// binding; the guard verifies the caller's JWT
/// <c>groups</c> claim contains <c>/fabs/&lt;fabId&gt;</c>.
///
/// <para>
/// Multi-fab users (e.g. a regional admin assigned to both
/// <c>/fabs/munich</c> and <c>/fabs/berlin</c>) are supported:
/// the guard only checks that the requested <c>fabId</c> is
/// present in the caller's group list, so each per-fab API call
/// passes independently.
/// </para>
///
/// <para>
/// Callers state the fab per request — with one recorded exception.
/// Automation's rule endpoints infer it when the operator belongs to
/// exactly one fab, and refuse a multi-fab operator who names none
/// (ADR-0114). That is scoped to those endpoints; everywhere else an
/// explicit <c>fabId</c> is required, and extending inference is a new
/// decision rather than an application of that one.
/// </para>
///
/// <para>
/// Enumerating which fabs a caller belongs to is deliberately not on this
/// interface — see <see cref="FabClaims"/>. This one answers a single
/// question, and widening it would grow every implementation and test
/// double with a method most callers never use.
/// </para>
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
        Ensure.That(user).IsNotNull();
        Ensure.That(fabId).IsNotNullOrWhiteSpace();

        cancellationToken.ThrowIfCancellationRequested();

        string targetGroup = FabGroupPrefix + fabId;
        foreach (Claim claim in user.FindAll(GroupClaimType))
        {
            // Keycloak emits group memberships as either repeated
            // single-value claims or one space-separated claim; split
            // defensively.
            string[] tokens = claim.Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
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
///
/// <para>
/// A caller who belongs to no fab at all is a different refusal, and
/// <see cref="ForNoFabMembership"/> says so. It used to be expressed by
/// passing the literal <c>"none"</c> through the guard, which reads back to
/// the operator — and into the logs — as though they had asked for a fab by
/// that name.
/// </para>
/// </summary>
public sealed class FabAuthorizationException : Exception
{
    public FabAuthorizationException(string fabId)
        : base($"Caller is not authorized to access fab '{fabId}'.") => FabId = fabId;

    private FabAuthorizationException()
        : base("Caller belongs to no fab.")
    {
    }

    /// <summary>
    /// The fab the caller was refused, or none when they belong to no fab and
    /// so never named one.
    /// </summary>
    public string FabId { get; }

    /// <summary>
    /// The caller holds no fab membership at all — a misconfigured account
    /// rather than an overreaching request.
    /// </summary>
    public static FabAuthorizationException ForNoFabMembership() => new();
}
