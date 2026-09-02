using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using Stream = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;

public sealed class GetStreamQueryHandler(IStreamQuerySource streams, IStreamWhepUrlBuilder whepUrls)
    : IQueryHandler<GetStreamQuery, Result<StreamHealthDto, GetStreamError>>
{
    public async Task<Result<StreamHealthDto, GetStreamError>> HandleAsync(GetStreamQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        (IReadOnlyList<FabIdentifier> fabs, CameraIdentifier camera) = query;

        // FR-006: the fab is part of the lookup rather than a check afterwards,
        // so a stream outside the caller's fabs and a camera with no stream at
        // all take the same path out of here and produce the same response.
        // FR-009 needs no special case either — a stream whose fab is still
        // NULL satisfies no IN clause, so it is returned to nobody.
        Stream? stream = await streams.Streams.SingleOrDefaultAsync(
            candidate => candidate.Camera == camera && fabs.Contains(candidate.Fab),
            cancellationToken);

        if (stream is null)
        {
            return Failure(GetStreamFailures.StreamNotFound(camera.Value));
        }

        return Success(Map(stream, whepUrls));
    }

    internal static StreamHealthDto Map(Stream stream, IStreamWhepUrlBuilder whepUrls) =>
        new(
            CameraIdentifier: stream.Camera.Value,
            // `Fab!` is safe: both query handlers filter on the caller's fabs,
            // and an unattributed stream matches no fab (FR-009), so a stream
            // that reaches this mapper has one.
            Fab: stream.Fab!.Value,
            State: stream.State.Value,
            WhepUrl: whepUrls.For(stream.Path),
            TranscodeMode: stream.TranscodeMode.Value,
            LastSuccessAt: stream.LastSuccessAt?.Value,
            Error: stream.LastError?.Value);
}
