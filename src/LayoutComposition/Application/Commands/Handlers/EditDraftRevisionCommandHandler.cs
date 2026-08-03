using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class EditDraftRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<EditDraftRevisionCommandHandler> logger)
    : ICommandHandler<EditDraftRevisionCommand, Result<LayoutRevisionNumber, EditDraftRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, EditDraftRevisionError>> HandleAsync(
        EditDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (LayoutIdentifier layoutIdentifier, LayoutRevisionNumber revisionNumber, GridDimensions grid, IReadOnlyList<Tile> tiles, int expectedVersion) = command;

        Option<GridViolation> violation = Layout.ValidateGrid(grid, tiles);
        if (violation.HasValue)
        {
            return Failure(EditDraftRevisionError.FromViolation(violation.Value));
        }

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(EditDraftRevisionFailures.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (layout.Version != expectedVersion)
        {
            return Failure(EditDraftRevisionFailures.LayoutRevisionStale(layoutIdentifier.Value, expectedVersion, layout.Version));
        }
        Revision? revision = layout.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Failure(EditDraftRevisionFailures.LayoutRevisionNotFound(
                    layoutIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Draft)
        {
            return Failure(EditDraftRevisionFailures.NotADraft(revision.State.Value));
        }

        layout.EditDraft(revisionNumber, grid, tiles, clock);
        await layouts.SaveAsync(cancellationToken);

        logger.EditedDraftRevision(revisionNumber, layout.Id);

        return Success(revisionNumber);
    }
}
