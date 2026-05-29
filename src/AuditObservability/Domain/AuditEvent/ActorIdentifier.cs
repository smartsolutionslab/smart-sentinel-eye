using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// Identity of the principal that triggered the audited event
/// (spec 009 FR-007). For interactive callers it carries the
/// Keycloak <c>sub</c> claim's Guid; for system-emitted events
/// (background reconcilers, the retention worker itself) it
/// resolves to <see cref="System"/>, a singleton wrapping
/// <see cref="Guid.Empty"/>.
/// </summary>
public sealed record ActorIdentifier : IValueObject<Guid>
{
    /// <summary>System-emitted events. Distinguished from human callers via <see cref="IsSystem"/>.</summary>
    public static ActorIdentifier System { get; } = new(Guid.Empty);

    public Guid Value { get; }

    public bool IsSystem => Value == Guid.Empty;

    private ActorIdentifier(Guid value) => Value = value;

    public static ActorIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException(
                $"ActorIdentifier cannot be empty for a human caller; use {nameof(ActorIdentifier)}.{nameof(System)} for system events.",
                nameof(value))
            : new ActorIdentifier(value);

    public sealed override string ToString() => Value.ToString();
}
