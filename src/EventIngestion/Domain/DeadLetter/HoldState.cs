using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

/// <summary>
/// Lifecycle state of a held dead letter (spec 317, #2325, FR-003). Every
/// captured row starts <see cref="Held"/>; the only transition is to
/// <see cref="Promoted"/>, and only for a <see cref="DeadLetterReason.UnknownEventType"/>
/// row — enforced by the predicate on <c>IDeadLetterRepository.PromoteHeldAsync</c>'s
/// set-based update, not by a method on this type (plan.md §2.1/§7 A7).
/// </summary>
public sealed record HoldState(string Value) : IValueObject<string>
{
    public static HoldState Held { get; } = new("Held");

    public static HoldState Promoted { get; } = new("Promoted");

    public static HoldState From(string value) => value switch
    {
        "Held" => Held,
        "Promoted" => Promoted,
        _ => throw new ArgumentException($"Unknown hold state '{value}'.", nameof(value)),
    };

    public sealed override string ToString() => Value;
}
