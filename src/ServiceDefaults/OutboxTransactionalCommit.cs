using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using Wolverine.EntityFrameworkCore;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Commits a context's tracked changes and the integration events captured
/// against it in one transaction, then releases the events (spec 021 FR-001).
///
/// <para>
/// This is the whole of ADR-0088's outbox reaching the write path. The outbox
/// was already configured — <c>PersistMessagesWithPostgresql</c>,
/// <c>UseEntityFrameworkCoreTransactions</c>, <c>AutoApplyTransactions</c> —
/// and it enrolled messages published from inside a Wolverine message handler.
/// None of the nine write paths is one, so nothing was enrolled and every
/// announcement left its transaction immediately.
/// </para>
///
/// <para>
/// The adapter is three lines because that is all that was missing. The
/// durability, the retry and the recovery after a crash are Wolverine's and
/// were paid for a year ago.
/// </para>
///
/// <para>
/// <b>What is durable, exactly (ADR-0185).</b> The transaction is shared by every
/// announcement, but only the message types a context declares in
/// <c>durableOutboxMessageTypes</c> have their outgoing envelope written to the
/// Postgres outbox, so they survive a broker outage or a crash after the commit.
/// Every other type is released after the commit and sent inline: it is never sent
/// for a write that rolled back, but it is lost on a crash or a failed send (a
/// broker outage). Local queues stay in memory.
/// </para>
/// </summary>
public sealed class OutboxTransactionalCommit<TDbContext>(IDbContextOutbox<TDbContext> outbox)
    : ITransactionalCommit
    where TDbContext : DbContext
{
    public Task CommitAsync(CancellationToken cancellationToken) =>
        outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
}
