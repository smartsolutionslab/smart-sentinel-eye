using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;

public sealed class ListStreamsQueryHandler(
    IStreamQuerySource streams,
    IStreamWhepUrlBuilder whepUrls)
    : IQueryHandler<ListStreamsQuery, Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>>
{
    public async Task<Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>> HandleAsync(
        ListStreamsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Cameras.Count > ListStreamsDefaults.MaximumBatchSize)
        {
            return Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>.Failure(
                new ListStreamsError.InvalidBatchSize(query.Cameras.Count, ListStreamsDefaults.MaximumBatchSize));
        }

        if (query.Cameras.Count == 0)
        {
            return Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>.Success(Array.Empty<StreamHealthDto>());
        }

        CameraIdentifier[] wanted = [.. query.Cameras];

        List<Stream> matches = await streams.Streams
            .Where(stream => wanted.Contains(stream.Camera))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<StreamHealthDto> dtos = matches
            .Select(stream => GetStreamQueryHandler.Map(stream, whepUrls))
            .ToList();

        return Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>.Success(dtos);
    }
}
