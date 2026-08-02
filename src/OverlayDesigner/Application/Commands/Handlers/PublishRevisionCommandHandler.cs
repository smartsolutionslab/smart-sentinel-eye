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
        Ensure.That(command).IsNotNull();
        (OverlayIdentifier overlayIdentifier, OverlayRevisionNumber revisionNumber, OperatorIdentifier publishedBy, int expectedVersion) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (overlay.Version != expectedVersion)
        {
            return Result<OverlayRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.OverlayRevisionStale(overlayIdentifier.Value, expectedVersion, overlay.Version));
        }
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
        await overlays.SaveAsync(cancellationToken);

        logger.PublishedRevision(overlay.Id, revisionNumber, publishedBy);

        return Result<OverlayRevisionNumber, PublishRevisionError>.Success(revisionNumber);
    }
}
