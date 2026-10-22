using Microsoft.AspNetCore.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

public sealed record ScopeRequirement(string Scope) : IAuthorizationRequirement;

public sealed class ScopeRequirementHandler(IAuthorizationDecisionPoint decisionPoint)
    : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        _ = decisionPoint;
        throw new NotImplementedException();
    }
}
