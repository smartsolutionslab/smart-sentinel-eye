namespace SmartSentinelEye.Shared.Kernel.Primitives;

/// <summary>
/// Base record for string-backed value objects per ADR-0046. Concrete types
/// validate via Ensure.That() inside their factory and pass the validated
/// string up.
/// </summary>
public abstract record StringValueObject(string Value) : IValueObject<string>
{
    public sealed override string ToString() => Value;
}
