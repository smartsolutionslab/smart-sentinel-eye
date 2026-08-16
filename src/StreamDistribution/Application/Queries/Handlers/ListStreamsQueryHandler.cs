using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;

public sealed class ListStreamsQueryHandler(IStreamQuerySource streams, IStreamWhepUrlBuilder whepUrls)
    : IQueryHandler<ListStreamsQuery, Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>>
{
    public async Task<Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>> HandleAsync(ListStreamsQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        (IReadOnlyList<FabIdentifier> fabs, IReadOnlyList<CameraIdentifier> cameras) = query;

        if (cameras.Count > ListStreamsDefaults.MaximumBatchSize)
        {
            return Failure(ListStreamsFailures.InvalidBatchSize(cameras.Count, ListStreamsDefaults.MaximumBatchSize));
        }

        if (cameras.Count == 0)
        {
            // Named explicitly: the outcome carries the expression's type, and
            // StreamHealthDto[] is not IReadOnlyList<StreamHealthDto> as far as
            // the conversion is concerned (generics are invariant).
            return Success<IReadOnlyList<StreamHealthDto>>([]);
        }

        CameraIdentifier[] wanted = [.. cameras];

        // FR-005 + FR-006: a stream in a fab the caller does not hold drops out
        // of the batch exactly like one that was never provisioned. FR-009 comes
        // free — an unattributed stream satisfies no IN clause.
        List<Stream> matches = await streams.Streams
            .Where(stream => wanted.Contains(stream.Camera) && fabs.Contains(stream.Fab))
            .ToListAsync(cancellationToken);

        IReadOnlyList<StreamHealthDto> dtos = matches.Select(stream => GetStreamQueryHandler.Map(stream, whepUrls)).ToList();

        return Success(dtos);
    }
}
