using Microsoft.AspNetCore.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

public sealed record ScopeRequirement(string Scope) : IAuthorizationRequirement;

public sealed class ScopeRequirementHandler(IAuthorizationDecisionPoint decisionPoint)
    : AuthorizationHandler<ScopeRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        var decision = await decisionPoint.DecideAsync(
            context.User,
            requirement.Scope,
            Option<string>.None,
            // Fine while no fab flows through here; use HttpContext.RequestAborted once a later slice passes a fab.
            CancellationToken.None);
        if (decision.IsAllowed)
        {
            context.Succeed(requirement);
        }
    }
}
