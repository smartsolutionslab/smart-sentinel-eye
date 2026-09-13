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

    public static RegistrationState From(string value) => value switch
    {
        "Registered" => Registered,
        "Retired" => Retired,
        _ => throw new ArgumentException($"Unknown registration state '{value}'.", nameof(value)),
    };

    public sealed override string ToString() => Value;
}
