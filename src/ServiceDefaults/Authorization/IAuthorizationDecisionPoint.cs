using System.Security.Claims;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// The single question every scope policy asks: may this caller do this,
/// optionally within this fab? A deny is an answer, never an exception
/// (ADR-0183).
/// </summary>
public interface IAuthorizationDecisionPoint
{
    Task<AuthorizationDecision> DecideAsync(
        ClaimsPrincipal principal,
        string scope,
        Option<string> fab,
        CancellationToken cancellationToken);
}

public enum AuthorizationDenyReason
{
    MissingScope,
    FabNotAuthorized,
}

public sealed record AuthorizationDecision(
    bool IsAllowed,
    AuthorizationDenyReason? Reason,
    Option<string> Fab)
{
    public static AuthorizationDecision Allow() => new(true, null, Option<string>.None);

    public static AuthorizationDecision Deny(AuthorizationDenyReason reason, Option<string> fab) =>
        new(false, reason, fab);
}

public sealed class DefaultAuthorizationDecisionPoint(IFabAuthorizationGuard guard) : IAuthorizationDecisionPoint
{
    public Task<AuthorizationDecision> DecideAsync(
        ClaimsPrincipal principal,
        string scope,
        Option<string> fab,
        CancellationToken cancellationToken)
    {
        _ = guard;
        throw new NotImplementedException();
    }
}
