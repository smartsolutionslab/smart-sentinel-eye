namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Marker for domain events per ADR-0040. Domain events are in-process only
/// and never cross a bounded context boundary; integration events do.
/// </summary>
public interface IDomainEvent;
