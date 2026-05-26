using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using Stream = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;

public sealed class GetStreamQueryHandler(
    IStreamQuerySource streams,
    IStreamWhepUrlBuilder whepUrls)
    : IQueryHandler<GetStreamQuery, Result<StreamHealthDto, GetStreamError>>
{
    public async Task<Result<StreamHealthDto, GetStreamError>> HandleAsync(
        GetStreamQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Stream? stream = await streams.Streams
            .SingleOrDefaultAsync(s => s.Camera == query.Camera, cancellationToken)
            .ConfigureAwait(false);

        if (stream is null)
        {
            return Result<StreamHealthDto, GetStreamError>.Failure(
                new GetStreamError.StreamNotFound(query.Camera.Value));
        }

        return Result<StreamHealthDto, GetStreamError>.Success(Map(stream, whepUrls));
    }

    internal static StreamHealthDto Map(Stream stream, IStreamWhepUrlBuilder whepUrls) =>
        new(
            CameraIdentifier: stream.Camera.Value,
            State: stream.State.Value,
            WhepUrl: whepUrls.For(stream.Path),
            TranscodeMode: stream.TranscodeMode.Value,
            LastSuccessAt: stream.LastSuccessAt,
            Error: stream.LastError);
}
