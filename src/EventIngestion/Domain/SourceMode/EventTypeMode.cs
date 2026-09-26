using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode;

/// <summary>
/// Admission policy for one <c>(fab, Source)</c> pair (decision 018,
/// spec.md FR-001). Two values, <c>strict</c> and <c>discovery</c>, lowercase
/// to match decision 018's own words and <see cref="Domain.Event.Source"/>'s
/// wire convention — it is also a wire token, in request bodies and in
/// <c>GET</c> output, unlike <c>RegistrationState</c> (plan.md §2).
///
/// </summary>
public sealed record EventTypeMode(string Value) : IValueObject<string>
{
    public static EventTypeMode Strict { get; } = new("strict");

    public static EventTypeMode Discovery { get; } = new("discovery");

    public static EventTypeMode From(string value) => value switch
    {
        "strict" => Strict,
        "discovery" => Discovery,
        _ => throw new ArgumentException($"Unknown event-type mode '{value}'.", nameof(value)),
    };

    public sealed override string ToString() => Value;
}
