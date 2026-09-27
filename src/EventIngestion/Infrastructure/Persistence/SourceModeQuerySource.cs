using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class SourceModeQuerySource(EventIngestionDbContext dbContext) : ISourceModeQuerySource
{
    public IQueryable<SourceMode> SourceModes => dbContext.SourceModes.AsNoTracking();
}
