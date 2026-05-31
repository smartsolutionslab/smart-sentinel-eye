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
        ArgumentNullException.ThrowIfNull(command);
        var (layoutIdentifier, branchedBy) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;
        if (!layout.Revisions.Any(revision => revision.State == LayoutRevisionState.Published))
        {
            return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(layoutIdentifier.Value));
        }

        Revision branched = layout.BranchDraft(branchedBy, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.BranchedDraftRevision(logger, branched.Number, layout.Id, branchedBy);

        return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Success(branched.Number);
    }
}
