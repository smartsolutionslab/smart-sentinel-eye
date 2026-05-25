namespace SmartSentinelEye.Shared.Kernel.Primitives;

/// <summary>
/// Marker for value objects per ADR-0066. Enables cross-cutting wiring
/// (JSON converters, EF value converters, OpenAPI schema generation) to be
/// registered once over all implementing types.
/// </summary>
public interface IValueObject;

/// <summary>
/// Marker carrying the primitive backing type.
/// </summary>
public interface IValueObject<out TValue> : IValueObject
{
    TValue Value { get; }
}
