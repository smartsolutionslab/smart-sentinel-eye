using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Aggregate root for an ingested fab event (spec 006). Single-write
/// — once ingested, immutable. The envelope is the aggregate; the
/// JSONB payload is opaque cargo.
///
/// <para>
/// Construction enforces the "occurredAt cannot be more than 5
/// minutes in the future" invariant from spec FR-014 (clocked against
/// <see cref="IClock"/> to keep the check testable). Dedup against
/// <c>(fab, identifier)</c> is the Application layer's job via
/// <see cref="IEventRepository.ExistsAsync"/>.
/// </para>
/// </summary>
public sealed class Event : AggregateRoot<EventIdentifier>
{
    public static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(5);

    public FabIdentifier Fab { get; private set; } = null!;

    public Source Source { get; private set; } = null!;

    public DeviceIdentifier Device { get; private set; } = null!;

    public Kind Kind { get; private set; } = null!;

    public OccurredAt OccurredAt { get; private set; } = null!;

    public IngestedAt IngestedAt { get; private set; } = null!;

    public Payload Payload { get; private set; } = null!;

    private Event() { }

    public static Event Ingest(
        EventIdentifier identifier,
        FabIdentifier fab,
        Source source,
        DeviceIdentifier device,
        Kind kind,
        OccurredAt occurredAt,
        Payload payload,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(fab);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(occurredAt);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(clock);

        DateTimeOffset now = clock.UtcNow;
        if (occurredAt.Value > now.Add(MaximumFutureSkew))
        {
            throw new ArgumentException(
                $"occurredAt cannot be more than {MaximumFutureSkew.TotalMinutes:N0} minutes " +
                $"in the future (got {occurredAt} vs now {now:O}).",
                nameof(occurredAt));
        }

        IngestedAt ingestedAt = IngestedAt.From(now);
        Event @event = new()
        {
            Id = identifier,
            Fab = fab,
            Source = source,
            Device = device,
            Kind = kind,
            OccurredAt = occurredAt,
            IngestedAt = ingestedAt,
            Payload = payload,
        };
        @event.Raise(new EventIngestedDomainEvent(
            identifier, fab, source, device, kind, occurredAt, ingestedAt, payload));
        return @event;
    }
}
