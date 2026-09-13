using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

/// <summary>
/// Stable identifier for a registered event type (ADR-0090). There is no
/// public-facing handle the way <c>WebhookIntegrationName</c> is one — the
/// registry has no single-resource GET, so this Guid v7 never appears in a
/// route, only in list bodies.
/// </summary>
public readonly record struct RegisteredEventTypeIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<RegisteredEventTypeIdentifier>
{
    public static RegisteredEventTypeIdentifier New() => new(Guid.CreateVersion7());

    public static RegisteredEventTypeIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public static implicit operator Guid(RegisteredEventTypeIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(RegisteredEventTypeIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(RegisteredEventTypeIdentifier left, RegisteredEventTypeIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(RegisteredEventTypeIdentifier left, RegisteredEventTypeIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(RegisteredEventTypeIdentifier left, RegisteredEventTypeIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(RegisteredEventTypeIdentifier left, RegisteredEventTypeIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
