using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class BranchDraftRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<BranchDraftRevisionCommandHandler> log)
    : ICommandHandler<BranchDraftRevisionCommand, Result<LayoutRevisionNumber, BranchDraftRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, BranchDraftRevisionError>> HandleAsync(
        BranchDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(command.Layout, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.LayoutNotFound(command.Layout.Value));
        }

        Layout layout = found.Value;
        if (!layout.Revisions.Any(r => r.State == LayoutRevisionState.Published))
        {
            return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(command.Layout.Value));
        }

        Revision branched = layout.BranchDraft(command.BranchedBy, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Branched draft revision {Revision} on layout {Layout} by {Operator}.",
            branched.Number, layout.Id, command.BranchedBy);

        return Result<LayoutRevisionNumber, BranchDraftRevisionError>.Success(branched.Number);
    }
}
