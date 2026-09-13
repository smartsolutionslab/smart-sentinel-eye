using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

/// <summary>
/// Lifecycle state of a registered event type (spec 143 FR-005). Two-state
/// machine: <c>Registered</c> → <c>Retired</c> (terminal; name freed for
/// re-use via the partial unique index).
/// </summary>
public sealed record RegistrationState(string Value) : IValueObject<string>
{
    public static RegistrationState Registered { get; } = new("Registered");

    public static RegistrationState Retired { get; } = new("Retired");

    // Phase 4a prelude (spec 143 T001): behaviour withheld on purpose. The
    // switch and the throw for an unknown state land in T004; until then this
    // always answers Registered, which is what keeps a CS0246 from being the
    // only way phase 4a's tests could fail (ADR-0144, plan.md §9).
    public static RegistrationState From(string value) => Registered;

    public sealed override string ToString() => Value;
}
