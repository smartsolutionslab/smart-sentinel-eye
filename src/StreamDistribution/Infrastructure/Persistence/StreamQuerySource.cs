using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.StreamDistribution.Application.Queries;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

/// <summary>
/// EF-Core-backed read-side seam (<see cref="IStreamQuerySource"/>). Uses
/// AsNoTracking to keep list queries cheap.
/// </summary>
public sealed class StreamQuerySource(StreamDistributionDbContext dbContext) : IStreamQuerySource
{
    public IQueryable<Domain.Stream.Stream> Streams => dbContext.Streams.AsNoTracking();
}
