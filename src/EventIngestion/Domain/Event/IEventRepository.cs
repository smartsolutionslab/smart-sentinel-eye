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

    void Add(Event @event);

    Task SaveAsync(CancellationToken cancellationToken);
}
