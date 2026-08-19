using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using Wolverine.EntityFrameworkCore;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Publishes an integration event into the outbox held against this context's
/// <see cref="DbContext"/>, so the message shares the fate of the write that
/// produced it (spec 021 FR-001).
///
/// <para>
/// This replaces a publisher that went straight to the broker. ADR-0088 already
/// mandates a Postgres outbox and <see cref="WolverineDefaults"/> genuinely
/// configures it — but <c>AutoApplyTransactions</c> enrols messages published
/// from inside a <b>Wolverine message handler</b>, and none of the nine write
/// paths is one. They are HTTP endpoints and hosted services, so nothing was
/// enrolled and every announcement left the transaction immediately. The outbox
/// was configured, paid for, and unreached.
/// </para>
///
/// <para>
/// <b>The interface cannot express what changed.</b> <c>PublishAsync</c> has the
/// same signature as before and a different promise: the message is now
/// <i>captured</i> rather than sent, and it leaves only when the surrounding
/// write commits. A caller cannot tell the two apart from the type, which is
/// exactly how the defect survived behind an ADR claiming otherwise — so the
/// obligation is written down in <c>contracts/event-bus.md</c> instead.
/// </para>
///
/// <para>
/// Generic in the <see cref="DbContext"/> because the outbox is bound to one:
/// the message must be written by the same transaction as the rows. Every
/// context registers its own through
/// <see cref="WolverineDefaults.AddWolverineForContext{TDbContext}"/>.
/// </para>
/// </summary>
public sealed class OutboxEventBus<TDbContext>(
    IDbContextOutbox<TDbContext> outbox,
    ILogger<OutboxEventBus<TDbContext>> logger) : IEventBus
    where TDbContext : DbContext
{
    public async Task PublishAsync<TEvent>(
        TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        logger.CapturingIntegrationEvent(typeof(TEvent).FullName);

        // Captured, not sent. It reaches the broker when the caller commits
        // through SaveChangesAndFlushMessagesAsync, and never if the caller's
        // transaction rolls back — which is the half of the guarantee a naive
        // fix gets wrong, because announcing a write that did not happen is
        // worse than losing the announcement of one that did.
        await outbox.PublishAsync(integrationEvent);
    }
}
