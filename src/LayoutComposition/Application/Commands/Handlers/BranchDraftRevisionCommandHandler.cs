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
        BranchDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (LayoutIdentifier layoutIdentifier, OperatorIdentifier branchedBy, int expectedVersion) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (layout.Version != expectedVersion)
        {
            return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.LayoutRevisionStale(layoutIdentifier.Value, expectedVersion, layout.Version));
        }
        if (!layout.Revisions.Any(revision => revision.State == LayoutRevisionState.Published))
        {
            return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(layoutIdentifier.Value));
        }

        Revision branched = layout.BranchDraft(branchedBy, clock);
        await layouts.SaveAsync(cancellationToken);

        logger.BranchedDraftRevision(branched.Number, layout.Id, branchedBy);

        return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Success(branched.Number);
    }
}
