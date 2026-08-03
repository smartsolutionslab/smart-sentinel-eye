using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class RevertRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<RevertRevisionCommandHandler> logger)
    : ICommandHandler<RevertRevisionCommand, Result<OverlayRevisionNumber, RevertRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, RevertRevisionError>> HandleAsync(
        RevertRevisionCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (OverlayIdentifier overlayIdentifier, OverlayRevisionNumber revisionNumber, OperatorIdentifier revertedBy, int expectedVersion) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(RevertRevisionFailures.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (overlay.Version != expectedVersion)
        {
            return Failure(RevertRevisionFailures.OverlayRevisionStale(overlayIdentifier.Value, expectedVersion, overlay.Version));
        }
        Revision? revision = overlay.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Failure(RevertRevisionFailures.OverlayRevisionNotFound(
                    overlayIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != OverlayRevisionState.Published)
        {
            return Failure(RevertRevisionFailures.NotPublished(revision.State.Value));
        }

        overlay.Revert(revisionNumber, revertedBy, clock);
        await overlays.SaveAsync(cancellationToken);

        logger.RevertedRevision(revisionNumber, overlay.Id, revertedBy);

        return Success(revisionNumber);
    }
}
