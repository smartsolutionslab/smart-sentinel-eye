using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class BranchDraftRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<BranchDraftRevisionCommandHandler> logger)
    : ICommandHandler<BranchDraftRevisionCommand, Result<LayoutRevisionNumber, BranchDraftRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, BranchDraftRevisionError>> HandleAsync(
        BranchDraftRevisionCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (IReadOnlyList<FabIdentifier> fabs, LayoutIdentifier layoutIdentifier, OperatorIdentifier branchedBy, int expectedVersion) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(fabs, layoutIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(BranchDraftRevisionFailures.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (layout.Version != expectedVersion)
        {
            return Failure(BranchDraftRevisionFailures.LayoutRevisionStale(layoutIdentifier.Value, expectedVersion, layout.Version));
        }
        // Spec 037 (ADR-0121). This refusal used to be "no Published revision",
        // which now covers two chains that deserve opposite answers: one with a
        // draft still open, and one archived into strandedness. Only the first
        // is still refused.
        if (layout.Revisions.All(revision => revision.State != LayoutRevisionState.Published))
        {
            Revision? openDraft = layout.Revisions
                .FirstOrDefault(revision => revision.State == LayoutRevisionState.Draft);
            if (openDraft is not null)
            {
                return Failure(BranchDraftRevisionFailures.NoPublishedRevisionToBranchFrom(
                    layoutIdentifier.Value, openDraft.Number.Value));
            }

            // Fully archived, so recoverable (FR-001) — but its name went free
            // when it stranded, and another layout may have taken it (FR-009).
            //
            // GetByNameAsync ignores fully-archived chains, so this layout is
            // excluded from its own lookup and any hit is necessarily a
            // different one. That is why no "excluding" argument is needed, and
            // it is also why this check MUST stay inside this branch: on the
            // Published path the chain is visible to the predicate, so it would
            // match itself and refuse every branch of every healthy layout.
            Option<Layout> holder = await layouts
                .GetByNameAsync(layout.Fab, layout.Name, cancellationToken);
            if (holder.HasValue)
            {
                return Failure(BranchDraftRevisionFailures.LayoutNameTaken(
                    layoutIdentifier.Value, layout.Name.Value, layout.Fab.Value));
            }
        }

        Revision branched = layout.BranchDraft(branchedBy, clock);
        await layouts.SaveAsync(cancellationToken);

        logger.BranchedDraftRevision(branched.Number, layout.Id, branchedBy);

        return Success(branched.Number);
    }
}
