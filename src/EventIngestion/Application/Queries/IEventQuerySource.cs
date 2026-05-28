using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

/// <summary>
/// Read-side IQueryable seam for events (ADR-0041). Infrastructure
/// backs it with the DbContext; Application stays EF-Core-free at
/// the call site so handler tests can substitute an in-memory
/// IQueryable.
/// </summary>
public interface IEventQuerySource
{
    IQueryable<EventAggregate> Events { get; }
}
