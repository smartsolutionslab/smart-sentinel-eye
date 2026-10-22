using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.StreamDistribution.Infrastructure;

/// <summary>
/// ADR-0182 (spec 436, #2783): a retire that loses an optimistic-concurrency race to the health sweep's save is
/// retried inline instead of dead-lettered, so a retired camera cannot be left with a live stream. ADR-0113 forbids
/// retrying a conflicting write in general; ADR-0182 permits it where all four conditions hold, and the retire meets
/// each of them:
/// (1) the transition is terminal - <c>Stream.Retire</c> never leaves Retired, and a health report is refused on a
/// retired stream;
/// (2) it is idempotent - retiring an already retired stream returns early;
/// (3) the target state comes from the message, not from the state that was read - <c>CameraRetiredV1</c> alone
/// decides it;
/// (4) every attempt reloads - <c>RetryWithCooldown</c> re-enters the handler, so each attempt gets a new scope and
/// DbContext and sees the sweep's committed row.
///
/// <para>
/// Scoped to the <see cref="CameraRetiredV1"/> chain and <see cref="DbUpdateConcurrencyException"/> only. Slots
/// exhausted (four attempts in all, 2.6 s of inline delay), Wolverine falls back to the error queue. Inline delays
/// cannot be cancelled, which is why the ladder stays in seconds.
/// </para>
/// </summary>
public sealed class RetireStreamFailurePolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        var chain = chains.FirstOrDefault(candidate => candidate.MessageType == typeof(CameraRetiredV1));

        chain?.OnException<DbUpdateConcurrencyException>()
            .RetryWithCooldown(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(2));
    }
}
