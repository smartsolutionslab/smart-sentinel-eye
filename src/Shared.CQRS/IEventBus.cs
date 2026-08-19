namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// Application-facing seam for publishing integration events (ADR-0040 +
/// ADR-0057 + ADR-0088). The implementation in ServiceDefaults wraps
/// Wolverine's IMessageBus — application code stays Wolverine-free.
///
/// <para>
/// <b>Publishing captures; it does not send</b> (spec 021). The message is held
/// against the caller's unit of work and released when that unit of work
/// commits, so a write and its announcement share a fate. The signature cannot
/// say this, which is why it is said here.
/// </para>
///
/// <para>
/// <b>Two things follow, and both have already caught us out.</b>
/// </para>
///
/// <para>
/// <i>Publish before you commit.</i> A message captured after the commit has
/// nothing left to release it and will sit in the outbox indefinitely. This is
/// not theoretical — <c>RotateWebhookClientCommandHandler</c> published after
/// its save and had to be given an explicit flush.
/// </para>
///
/// <para>
/// <i>A publish with no accompanying write still needs a flush.</i> There is no
/// commit to ride on, so the caller must call
/// <see cref="ITransactionalCommit.CommitAsync"/> itself.
/// <c>AuditRetentionHostedService</c> announces an archived chunk without
/// writing through EF at all, and its announcement stopped arriving until it
/// did this. The atomicity is vacuous there — no row to share a fate with — but
/// the durability is not: the send survives a crash, which the previous
/// straight-to-broker publish did not.
/// </para>
///
/// <para>
/// Inside a Wolverine message handler neither applies: those are enrolled
/// automatically by <c>AutoApplyTransactions</c> (ADR-0088).
/// </para>
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
