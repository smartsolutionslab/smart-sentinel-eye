using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class RevertRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<RevertRevisionCommandHandler> logger)
    : ICommandHandler<RevertRevisionCommand, Result<LayoutRevisionNumber, RevertRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, RevertRevisionError>> HandleAsync(
        RevertRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (layoutIdentifier, revisionNumber, revertedBy) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;
        Revision? revision = layout.Revisions.SingleOrDefault(r => r.Number == revisionNumber);
        if (revision is null)
        {
            return Result<LayoutRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.LayoutRevisionNotFound(
                    layoutIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Published)
        {
            return Result<LayoutRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.NotPublished(revision.State.Value));
        }

        layout.Revert(revisionNumber, revertedBy, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Reverted revision {Revision} on layout {Layout} to Draft by {Operator}.",
            revisionNumber, layout.Id, revertedBy);

        return Result<LayoutRevisionNumber, RevertRevisionError>.Success(revisionNumber);
    }
}
