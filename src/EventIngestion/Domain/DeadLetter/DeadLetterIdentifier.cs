using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

public readonly record struct DeadLetterIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<DeadLetterIdentifier>
{
    public static DeadLetterIdentifier New() => new(Guid.CreateVersion7());

    public static DeadLetterIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("DeadLetterIdentifier cannot be empty.", nameof(value))
            : new DeadLetterIdentifier(value);

    public static implicit operator Guid(DeadLetterIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(DeadLetterIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(DeadLetterIdentifier left, DeadLetterIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(DeadLetterIdentifier left, DeadLetterIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(DeadLetterIdentifier left, DeadLetterIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(DeadLetterIdentifier left, DeadLetterIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
