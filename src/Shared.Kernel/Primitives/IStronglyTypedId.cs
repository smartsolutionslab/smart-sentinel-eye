namespace SmartSentinelEye.Shared.Kernel.Primitives;

/// <summary>
/// Marker for strongly-typed identifier value objects per ADR-0090. Combines
/// with IValueObject&lt;TValue&gt; for cross-cutting tooling.
/// </summary>
public interface IStronglyTypedId<out TValue> : IValueObject<TValue>;
