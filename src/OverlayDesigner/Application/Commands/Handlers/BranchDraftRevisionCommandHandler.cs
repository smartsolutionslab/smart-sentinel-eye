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
        BranchDraftRevisionCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (OverlayIdentifier overlayIdentifier, OperatorIdentifier branchedBy, int expectedVersion) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(BranchDraftRevisionFailures.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (overlay.Version != expectedVersion)
        {
            return Failure(BranchDraftRevisionFailures.OverlayRevisionStale(overlayIdentifier.Value, expectedVersion, overlay.Version));
        }
        // Spec 037 (ADR-0121). This refusal used to be "no Published revision",
        // which now covers two chains that deserve opposite answers: one with a
        // draft still open, and one archived into strandedness. Only the first
        // is still refused.
        if (overlay.Revisions.All(revision => revision.State != OverlayRevisionState.Published))
        {
            Revision? openDraft = overlay.Revisions
                .FirstOrDefault(revision => revision.State == OverlayRevisionState.Draft);
            if (openDraft is not null)
            {
                return Failure(BranchDraftRevisionFailures.NoPublishedRevisionToBranchFrom(
                    overlayIdentifier.Value, openDraft.Number.Value));
            }

            // Fully archived, so recoverable (FR-001) — but its name went free
            // when it stranded, and another overlay may have taken it (FR-009).
            //
            // GetByNameAsync ignores fully-archived chains, so this overlay is
            // excluded from its own lookup and any hit is necessarily a
            // different one. That is why no "excluding" argument is needed, and
            // it is also why this check MUST stay inside this branch: on the
            // Published path the chain is visible to the predicate, so it would
            // match itself and refuse every branch of every healthy overlay.
            Option<Overlay> holder = await overlays.GetByNameAsync(overlay.Name, cancellationToken);
            if (holder.HasValue)
            {
                return Failure(BranchDraftRevisionFailures.OverlayNameTaken(
                    overlayIdentifier.Value, overlay.Name.Value));
            }
        }

        Revision branched = overlay.BranchDraft(branchedBy, clock);
        await overlays.SaveAsync(cancellationToken);

        logger.BranchedDraftRevision(branched.Number, overlay.Id, branchedBy);

        return Success(branched.Number);
    }
}
