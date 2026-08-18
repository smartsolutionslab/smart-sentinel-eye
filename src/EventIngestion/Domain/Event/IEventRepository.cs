using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Event repository contract (ADR-0041). Implementation lives in
/// EventIngestion.Infrastructure. <see cref="ExistsAsync"/> backs
/// the hybrid-idempotency check (spec 006 FR-002) without round-
/// tripping the full envelope.
/// </summary>
public interface IEventRepository
{
    Task<Option<Event>> GetByIdentifierAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken);

    /// <summary>
    /// Which of these (fab, identifier) pairs are already stored, in one query
    /// rather than one per event.
    ///
    /// <para>
    /// Spec 020 FR-010: ingest must not become a round trip per event, and the
    /// idempotency check was one of the two that made it so. Takes the fab as
    /// well as the identifier even though a Guid v7 identifier is unique on its
    /// own, because the fab is the partition key — without it the lookup reads
    /// every plant's partition to answer a question about one.
    /// </para>
    /// </summary>
    Task<IReadOnlySet<EventIdentifier>> ExistingAsync(
        IReadOnlyCollection<(FabIdentifier Fab, EventIdentifier Identifier)> candidates,
        CancellationToken cancellationToken);

    void Add(Event @event);

    Task SaveAsync(CancellationToken cancellationToken);
}
