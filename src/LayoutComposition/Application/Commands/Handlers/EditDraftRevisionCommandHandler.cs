using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Application.Tiles;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class EditDraftRevisionCommandHandler(
    ILayoutRepository layouts,
    ICameraFabGuard cameraFabs,
    IClock clock,
    ILogger<EditDraftRevisionCommandHandler> logger)
    : ICommandHandler<EditDraftRevisionCommand, Result<LayoutRevisionNumber, EditDraftRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, EditDraftRevisionError>> HandleAsync(
        EditDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (IReadOnlyList<FabIdentifier> fabs, LayoutIdentifier layoutIdentifier, LayoutRevisionNumber revisionNumber, GridDimensions grid, IReadOnlyList<Tile> tiles, int expectedVersion) = command;

        Option<GridViolation> violation = Layout.ValidateGrid(grid, tiles);
        if (violation.HasValue)
        {
            return Failure(EditDraftRevisionError.FromViolation(violation.Value));
        }

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(fabs, layoutIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(EditDraftRevisionFailures.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;

        // FR-014 again on the edit path, and this is the half that would be
        // easy to miss: creation refusing a cross-fab tile is worth nothing if
        // an edit can introduce one afterwards. Checked against the *layout's*
        // fab, not the caller's fabs — a multi-fab operator editing a dresden
        // layout may still only use dresden's cameras.
        IReadOnlyList<CameraIdentifier> outside = await cameraFabs
            .CamerasOutsideFabAsync(layout.Fab, [.. tiles.Select(tile => tile.Camera)], cancellationToken);
        if (outside.Count > 0)
        {
            logger.RefusedCrossFabTiles(layout.Fab, outside.Count);
            return Failure(EditDraftRevisionFailures.TileCameraOutsideFab(
                layout.Fab.Value, [.. outside.Select(camera => camera.Value)]));
        }

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
