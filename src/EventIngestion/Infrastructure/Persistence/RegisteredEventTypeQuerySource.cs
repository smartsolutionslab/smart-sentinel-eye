using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class RegisteredEventTypeQuerySource(EventIngestionDbContext dbContext)
    : IRegisteredEventTypeQuerySource
{
    public IQueryable<RegisteredEventType> RegisteredEventTypes =>
        dbContext.RegisteredEventTypes.AsNoTracking();
}
