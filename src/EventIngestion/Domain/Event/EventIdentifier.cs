using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Stable, sortable identifier for an ingested event (ADR-0090).
/// For PLC + inference sources the device supplies a Guid v7 in the
/// MQTT payload; for manual + webhook sources the server mints one
/// (spec 006 FR-002).
/// </summary>
public readonly record struct EventIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static EventIdentifier New() => new(Guid.CreateVersion7());

    public static EventIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("EventIdentifier cannot be empty.", nameof(value))
            : new EventIdentifier(value);

    public override string ToString() => Value.ToString();
}
