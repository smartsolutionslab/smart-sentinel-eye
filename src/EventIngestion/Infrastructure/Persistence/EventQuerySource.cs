using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.Queries;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class EventQuerySource(EventIngestionDbContext dbContext) : IEventQuerySource
{
    public IQueryable<EventAggregate> Events => dbContext.Events.AsNoTracking();
}
