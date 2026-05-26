using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

public sealed class StreamRepository(StreamDistributionDbContext dbContext) : IStreamRepository
{
    public async Task<Option<Domain.Stream.Stream>> GetByIdentifierAsync(
        StreamIdentifier stream, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = await dbContext.Streams
            .FirstOrDefaultAsync(candidate => candidate.Id == stream, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Domain.Stream.Stream>.None : Option<Domain.Stream.Stream>.Some(found);
    }

    public async Task<Option<Domain.Stream.Stream>> GetByCameraAsync(
        CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = await dbContext.Streams
            .FirstOrDefaultAsync(candidate => candidate.Camera == camera, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Domain.Stream.Stream>.None : Option<Domain.Stream.Stream>.Some(found);
    }

    public async Task<Option<Domain.Stream.Stream>> GetByPathAsync(
        MediaMtxPath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        Domain.Stream.Stream? found = await dbContext.Streams
            .FirstOrDefaultAsync(candidate => candidate.Path == path, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Domain.Stream.Stream>.None : Option<Domain.Stream.Stream>.Some(found);
    }

    public void Add(Domain.Stream.Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        dbContext.Streams.Add(stream);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
