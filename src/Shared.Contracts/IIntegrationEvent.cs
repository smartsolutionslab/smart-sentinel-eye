namespace SmartSentinelEye.Shared.Contracts;

/// <summary>
/// Marker for cross-context integration events per ADR-0040. Every
/// integration event carries an explicit V&lt;N&gt; suffix in its type name
/// per ADR-0073, and a common <see cref="EventMetadata"/> envelope per
/// ADR-0102 (who / when / which fab + a stable idempotency key).
/// </summary>
public interface IIntegrationEvent
{
    EventMetadata Metadata { get; }
}
