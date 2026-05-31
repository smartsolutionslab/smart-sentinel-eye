using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class BranchDraftRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<BranchDraftRevisionCommandHandler> logger)
    : ICommandHandler<BranchDraftRevisionCommand, Result<OverlayRevisionNumber, BranchDraftRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, BranchDraftRevisionError>> HandleAsync(
        BranchDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (overlayIdentifier, branchedBy) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;
        if (!overlay.Revisions.Any(revision => revision.State == OverlayRevisionState.Published))
        {
            return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(overlayIdentifier.Value));
        }

        Revision branched = overlay.BranchDraft(branchedBy, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Branched draft revision {Revision} on overlay {Overlay} by {Operator}.",
            branched.Number, overlay.Id, branchedBy);

        return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Success(branched.Number);
    }
}
