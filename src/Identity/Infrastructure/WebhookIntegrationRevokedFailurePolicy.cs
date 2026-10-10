using JasperFx;
using JasperFx.CodeGeneration;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.Identity.Infrastructure;

/// <summary>
/// Spec 328 (#2629): a Keycloak outage must not dead-letter a webhook-client
/// revocation after Wolverine's default three immediate attempts, because a
/// dead-lettered revocation leaves the client enabled — and, per ADR-0160, its
/// tokens accepted — until someone replays it by hand. Scoped to exactly
/// <see cref="WebhookIntegrationRevokedV1"/>'s own handler chain, mirroring
/// <c>WallSceneSwitchFailurePolicy</c> (spec 296) — no shared default.
///
/// <para>
/// Seam only: the policy is registered (<c>IdentityInfrastructureModule</c>.
/// <c>ConfigureMessageHandling</c>) but <see cref="Apply"/> adds no rule yet.
/// The retry ladder lands in a later commit (plan.md §5.0/§6).
/// </para>
/// </summary>
public sealed class WebhookIntegrationRevokedFailurePolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
    }
}
