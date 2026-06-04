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
        (LayoutIdentifier layoutIdentifier, LayoutRevisionNumber revisionNumber, GridDimensions grid, IReadOnlyList<Tile> tiles) = command;

        Option<GridViolation> violation = Layout.ValidateGrid(grid, tiles);
        if (violation.HasValue)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                EditDraftRevisionError.FromViolation(violation.Value));
        }

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;
        Revision? revision = layout.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.LayoutRevisionNotFound(
                    layoutIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Draft)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.NotADraft(revision.State.Value));
        }

        layout.EditDraft(revisionNumber, grid, tiles, clock);
        await layouts.SaveAsync(cancellationToken);

        logger.EditedDraftRevision(revisionNumber, layout.Id);

        return Result<LayoutRevisionNumber, EditDraftRevisionError>.Success(revisionNumber);
    }
}
