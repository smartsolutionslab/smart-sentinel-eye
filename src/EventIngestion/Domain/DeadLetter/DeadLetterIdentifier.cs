using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

public readonly record struct DeadLetterIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static DeadLetterIdentifier New() => new(Guid.CreateVersion7());

    public static DeadLetterIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("DeadLetterIdentifier cannot be empty.", nameof(value))
            : new DeadLetterIdentifier(value);

    public override string ToString() => Value.ToString();
}
