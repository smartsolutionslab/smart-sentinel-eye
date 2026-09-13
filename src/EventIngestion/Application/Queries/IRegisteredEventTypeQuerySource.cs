using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

public interface IRegisteredEventTypeQuerySource
{
    IQueryable<RegisteredEventType> RegisteredEventTypes { get; }
}
