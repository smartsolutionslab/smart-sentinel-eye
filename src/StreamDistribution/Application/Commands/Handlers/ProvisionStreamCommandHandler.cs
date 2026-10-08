using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class ProvisionStreamCommandHandler(
    IStreamRepository streams,
    IRtspGateway rtsp,
    IClock clock,
    ILogger<ProvisionStreamCommandHandler> logger)
    : ICommandHandler<ProvisionStreamCommand, Result<StreamIdentifier, ProvisionStreamError>>
{
    public async Task<Result<StreamIdentifier, ProvisionStreamError>> HandleAsync(
        ProvisionStreamCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (FabIdentifier fab, CameraIdentifier camera, string? rtspSourceUrl, OperatorIdentifier provisionedBy) = command;

        if (string.IsNullOrWhiteSpace(rtspSourceUrl))
        {
            return Failure(ProvisionStreamFailures.InvalidRtspSource("source URL is required"));
        }

        // Re-validated at the trust boundary: the URL arrives as a primitive
        // from CameraCatalog, so its invariants are asserted again on the way in.
        StreamSourceUrl sourceUrl;
        try
        {
            sourceUrl = StreamSourceUrl.From(rtspSourceUrl);
        }
        catch (ArgumentException ex)
        {
            return Failure(ProvisionStreamFailures.InvalidRtspSource(ex.Message));
        }

        Option<Stream> existing = await streams.GetByCameraAsync(camera, cancellationToken);

        if (existing.HasValue)
        {
            Stream existingStream = existing.Value;
            logger.StreamAlreadyExists(camera);

            if (existingStream.State == StreamState.Retired)
            {
                // Symmetric with the race this handler now has to survive: a
                // redelivery for an already-retired row finishes the teardown
                // a failed (or crashed) compensation left behind, rather than
                // leaving a leftover MediaMTX path for a restart to find
                // (spec 318 US2).
                return await RemoveLeftoverPathAsync(existingStream, cancellationToken);
            }

            // A redelivery onto an already-saved row must re-assert the path
            // rather than short-circuit: after the reorder below, "row saved,
            // path not added" is the new partial state a failed add leaves
            // behind, and only a redelivery resolves it before the next
            // restart (spec 309 FR-004).
            return await RegisterPathAsync(existingStream, cancellationToken);
        }

        // Saved first so a failed save cannot strand a live MediaMTX path with
        // no row behind it, which the WHEP hook would admit for any fab
        // (spec 309 FR-001). A failed add after the save is unfinished work
        // the outbox redelivers, and the existing-row branch above finishes
        // it.
        Stream stream = Stream.Provision(fab, camera, sourceUrl, provisionedBy, clock);
        streams.Add(stream);
        await streams.SaveAsync(cancellationToken);

        return await RegisterPathAsync(stream, cancellationToken);
    }

    private async Task<Result<StreamIdentifier, ProvisionStreamError>> RegisterPathAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            await rtsp.AddPathAsync(stream.Path, stream.SourceUrl.Value, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.PathRegistrationFailed(ex, stream.Camera);
            return Failure(ProvisionStreamFailures.RtspGatewayUnavailable(ex.Message));
        }

        // Checked only after the add: a match then proves that any retire's
        // removal of this path commits after the add, never before it — the
        // removal wins either way (spec 318 §1.3).
        if (!await streams.IsUnchangedSinceLoadAsync(stream, cancellationToken))
        {
            StreamState committed = await streams.ReadCommittedStateAsync(stream.Id, cancellationToken);

            if (committed == StreamState.Retired)
            {
                return await YieldToRetirementAsync(stream, cancellationToken);
            }

            // Moved by a health report or a re-point, not a retirement: the
            // path is still wanted (spec 318 §9 A3).
        }

        logger.ProvisionedStream(stream.Id, stream.Camera, stream.Path);

        return Success(stream.Id);
    }

    private async Task<Result<StreamIdentifier, ProvisionStreamError>> YieldToRetirementAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            await rtsp.RemovePathAsync(stream.Path, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.ProvisionCompensationFailed(ex, stream.Camera);
            return Failure(ProvisionStreamFailures.RtspGatewayUnavailable(ex.Message));
        }

        logger.ProvisionYieldedToRetirement(stream.Id, stream.Camera, stream.Path);

        return Success(stream.Id);
    }

    private async Task<Result<StreamIdentifier, ProvisionStreamError>> RemoveLeftoverPathAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            await rtsp.RemovePathAsync(stream.Path, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.ProvisionCompensationFailed(ex, stream.Camera);
            return Failure(ProvisionStreamFailures.RtspGatewayUnavailable(ex.Message));
        }

        return Success(stream.Id);
    }
}
