using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class PublishRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<PublishRevisionCommandHandler> logger)
    : ICommandHandler<PublishRevisionCommand, Result<OverlayRevisionNumber, PublishRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, PublishRevisionError>> HandleAsync(
        PublishRevisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (overlayIdentifier, revisionNumber, publishedBy) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;
        Revision? revision = overlay.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Result<OverlayRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.OverlayRevisionNotFound(
                    overlayIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != OverlayRevisionState.Draft)
        {
            return Result<OverlayRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.InvalidStateTransition(revision.State.Value));
        }

        overlay.Publish(revisionNumber, publishedBy, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.PublishedRevision(logger, overlay.Id, revisionNumber, publishedBy);

        return Result<OverlayRevisionNumber, PublishRevisionError>.Success(revisionNumber);
    }
}
