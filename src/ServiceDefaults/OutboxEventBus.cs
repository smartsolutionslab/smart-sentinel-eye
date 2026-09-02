using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Publishes an integration event so that it shares the fate of the work that
/// produced it (spec 021 FR-001) — which means two different things depending
/// on where the caller is, and getting that wrong loses messages silently.
///
/// <para>
/// <b>Inside a Wolverine message handler</b>, the ambient
/// <see cref="IMessageContext"/> is already enrolled in the handler's
/// transaction by <c>AutoApplyTransactions</c> (ADR-0088) and flushed by
/// Wolverine when the handler completes. Publishing there is correct and needs
/// nothing from us.
/// </para>
///
/// <para>
/// <b>Everywhere else</b> — an HTTP endpoint or a hosted service calling a
/// repository — nothing enrols anything, which is the gap issue #1605 was
/// about. Those publishes go into the outbox bound to the calling context's
/// <see cref="DbContext"/>, and are released when the repository commits
/// through <see cref="ITransactionalCommit"/>.
/// </para>
///
/// <para>
/// <b>The distinction is not cosmetic.</b> <c>IDbContextOutbox&lt;T&gt;</c> is a
/// <i>separate</i> message context from the ambient one, so routing a Wolverine
/// handler's publish through it captures the message somewhere nobody flushes:
/// the scope disposes and the announcement is gone, with no error and no outbox
/// row. The first version of this class did exactly that, and it would have
/// taken out rule fan-out — a PLC event fires a rule, and neither the variable
/// set nor the overlay highlight is ever published.
/// </para>
/// </summary>
public sealed class OutboxEventBus<TDbContext>(
    IDbContextOutbox<TDbContext> outbox,
    IMessageContext ambient,
    ILogger<OutboxEventBus<TDbContext>> logger) : IEventBus
    where TDbContext : DbContext
{
    /// <remarks>
    /// <paramref name="cancellationToken"/> is honoured before the publish and
    /// not during it: Wolverine's PublishAsync takes no token, and capturing a
    /// message into an in-memory context is not an operation there is anything
    /// to cancel. Checking first means a caller who has already cancelled does
    /// not add to a unit of work it is abandoning (ADR-0049).
    /// </remarks>
    public async Task PublishAsync<TEvent>(
        TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Non-null only while a message is being handled — Wolverine's own
        // documented discriminator, rather than a guess about the call stack.
        if (ambient.Envelope is not null)
        {
            logger.PublishingIntegrationEvent(typeof(TEvent).FullName ?? typeof(TEvent).Name);
            await ambient.PublishAsync(integrationEvent);
            return;
        }

        logger.CapturingIntegrationEvent(typeof(TEvent).FullName ?? typeof(TEvent).Name);

        // Captured, not sent. It reaches the broker when the caller commits,
        // and never if the caller's transaction rolls back — which is the half
        // of the guarantee a naive fix gets wrong, because announcing a write
        // that did not happen is worse than losing the announcement of one that
        // did.
        await outbox.PublishAsync(integrationEvent);
    }
}
