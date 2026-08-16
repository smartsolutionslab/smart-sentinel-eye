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
        Ensure.That(command).IsNotNull();
        (IReadOnlyList<FabIdentifier> fabs, LayoutIdentifier layoutIdentifier, LayoutRevisionNumber revisionNumber, OperatorIdentifier revertedBy, int expectedVersion) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(fabs, layoutIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(RevertRevisionFailures.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (layout.Version != expectedVersion)
        {
            return Failure(RevertRevisionFailures.LayoutRevisionStale(layoutIdentifier.Value, expectedVersion, layout.Version));
        }
        Revision? revision = layout.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Failure(RevertRevisionFailures.LayoutRevisionNotFound(
                    layoutIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Published)
        {
            return Failure(RevertRevisionFailures.NotPublished(revision.State.Value));
        }

        layout.Revert(revisionNumber, revertedBy, clock);
        await layouts.SaveAsync(cancellationToken);

        logger.RevertedRevision(revisionNumber, layout.Id, revertedBy);

        return Success(revisionNumber);
    }
}
