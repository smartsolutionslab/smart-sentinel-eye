using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class DeadLetterQuerySource(EventIngestionDbContext dbContext) : IDeadLetterQuerySource
{
    public IQueryable<DeadLetter> DeadLetters => dbContext.DeadLetters.AsNoTracking();
}
