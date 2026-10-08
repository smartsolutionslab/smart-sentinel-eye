using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

/// <summary>
/// Why a <see cref="DeadLetter"/> row is held (spec 317, #2325, FR-001):
/// a delivery that never parsed, a parsed envelope a strict source refused,
/// or a parsed envelope a declared discovery source held for an unregistered
/// kind. Closed, mirroring <c>RegistrationState</c> / <c>EventTypeMode</c>.
/// </summary>
public sealed record DeadLetterReason(string Value) : IValueObject<string>
{
    public static DeadLetterReason ParseFailure { get; } = new("ParseFailure");

    public static DeadLetterReason Refused { get; } = new("Refused");

    public static DeadLetterReason UnknownEventType { get; } = new("UnknownEventType");

    public static DeadLetterReason From(string value) => value switch
    {
        "ParseFailure" => ParseFailure,
        "Refused" => Refused,
        "UnknownEventType" => UnknownEventType,
        _ => throw new ArgumentException($"Unknown dead-letter reason '{value}'.", nameof(value)),
    };

    public sealed override string ToString() => Value;
}
