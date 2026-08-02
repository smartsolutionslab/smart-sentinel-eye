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
        Ensure.That(command).IsNotNull();
        (OverlayIdentifier overlayIdentifier, OperatorIdentifier branchedBy, int expectedVersion) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (overlay.Version != expectedVersion)
        {
            return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.OverlayRevisionStale(overlayIdentifier.Value, expectedVersion, overlay.Version));
        }
        if (!overlay.Revisions.Any(revision => revision.State == OverlayRevisionState.Published))
        {
            return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(overlayIdentifier.Value));
        }

        Revision branched = overlay.BranchDraft(branchedBy, clock);
        await overlays.SaveAsync(cancellationToken);

        logger.BranchedDraftRevision(branched.Number, overlay.Id, branchedBy);

        return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Success(branched.Number);
    }
}
