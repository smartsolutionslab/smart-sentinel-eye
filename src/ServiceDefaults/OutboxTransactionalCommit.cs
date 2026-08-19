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
/// </summary>
public sealed class OutboxTransactionalCommit<TDbContext>(IDbContextOutbox<TDbContext> outbox)
    : ITransactionalCommit
    where TDbContext : DbContext
{
    public Task CommitAsync(CancellationToken cancellationToken) =>
        outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
}
