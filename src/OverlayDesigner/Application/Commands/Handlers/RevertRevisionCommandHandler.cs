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
        ArgumentNullException.ThrowIfNull(command);
        var (overlayIdentifier, revisionNumber, revertedBy) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;
        Revision? revision = overlay.Revisions.SingleOrDefault(r => r.Number == revisionNumber);
        if (revision is null)
        {
            return Result<OverlayRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.OverlayRevisionNotFound(
                    overlayIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != OverlayRevisionState.Published)
        {
            return Result<OverlayRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.NotPublished(revision.State.Value));
        }

        overlay.Revert(revisionNumber, revertedBy, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Reverted revision {Revision} on overlay {Overlay} to Draft by {Operator}.",
            revisionNumber, overlay.Id, revertedBy);

        return Result<OverlayRevisionNumber, RevertRevisionError>.Success(revisionNumber);
    }
}
