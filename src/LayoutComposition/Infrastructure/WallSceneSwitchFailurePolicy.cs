using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.LayoutComposition.Infrastructure;

/// <summary>
/// FR-011 (spec 296): a lost optimistic-concurrency race in
/// <c>WallSceneSwitchRequestedV1Handler</c> must never be retried — ADR-0113
/// forbids automatic retry of a conflicting write in terms, and retrying
/// here would re-apply a decision against freshly-loaded state, which is
/// exactly the defect that correction names. The handler lets
/// <see cref="DbUpdateConcurrencyException"/> escape uncaught (see its own
/// doc); this policy is what turns that escape into a dead letter instead
/// of Wolverine's default retry-with-backoff.
///
/// <para>
/// The first Wolverine failure rule in this repository (no
/// <c>OnException</c>/<c>RetryWith</c> existed before — spec A3), so it is
/// scoped to exactly <see cref="WallSceneSwitchRequestedV1"/>'s own handler
/// chain rather than changing any shared default.
/// </para>
/// </summary>
public sealed class WallSceneSwitchFailurePolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        HandlerChain? chain = chains.FirstOrDefault(
            candidate => candidate.MessageType == typeof(WallSceneSwitchRequestedV1));

        chain?.OnException<DbUpdateConcurrencyException>().MoveToErrorQueue();
    }
}
