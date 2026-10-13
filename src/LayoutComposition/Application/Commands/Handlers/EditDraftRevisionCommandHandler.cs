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
        (var fabs, var layoutIdentifier, var revisionNumber, var grid, var tiles, var expectedVersion) = command;

        var violation = Layout.ValidateGrid(grid, tiles);
        if (violation.HasValue)
        {
            return Failure(EditDraftRevisionError.FromViolation(violation.Value));
        }

        var found = await layouts
            .GetByIdentifierAsync(fabs, layoutIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(EditDraftRevisionFailures.LayoutNotFound(layoutIdentifier.Value));
        }

        var layout = found.Value;

        var crossFab = await RefuseCrossFabTilesAsync(layout, tiles, cancellationToken);
        if (crossFab.HasValue)
        {
            return Failure(crossFab.Value);
        }

        var revisionRefusal = RefuseRevision(layout, layoutIdentifier, revisionNumber, expectedVersion);
        if (revisionRefusal.HasValue)
        {
            return Failure(revisionRefusal.Value);
        }

        layout.EditDraft(revisionNumber, grid, tiles, clock);
        await layouts.SaveAsync(cancellationToken);

        logger.EditedDraftRevision(revisionNumber, layout.Id);

        return Success(revisionNumber);
    }

    // FR-014 again on the edit path, and this is the half that would be
    // easy to miss: creation refusing a cross-fab tile is worth nothing if
    // an edit can introduce one afterwards. Checked against the *layout's*
    // fab, not the caller's fabs — a multi-fab operator editing a dresden
    // layout may still only use dresden's cameras.
    private async Task<Option<EditDraftRevisionError>> RefuseCrossFabTilesAsync(
        Layout layout,
        IReadOnlyList<Tile> tiles,
        CancellationToken cancellationToken)
    {
        var outside = await cameraFabs
            .CamerasOutsideFabAsync(layout.Fab, [.. tiles.Select(tile => tile.Camera)], cancellationToken);
        if (outside.Count == 0)
        {
            return Option<EditDraftRevisionError>.None;
        }

        logger.RefusedCrossFabTiles(layout.Fab, outside.Count);
        return Option<EditDraftRevisionError>.Some(EditDraftRevisionFailures.TileCameraOutsideFab(
            layout.Fab.Value, [.. outside.Select(camera => camera.Value)]));
    }

    // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
    // has since moved. Checked before any mutation so nothing is applied
    // on top of stale intent.
    private static Option<EditDraftRevisionError> RefuseRevision(
        Layout layout,
        LayoutIdentifier layoutIdentifier,
        LayoutRevisionNumber revisionNumber,
        int expectedVersion)
    {
        if (layout.Version != expectedVersion)
        {
            return Option<EditDraftRevisionError>.Some(
                EditDraftRevisionFailures.LayoutRevisionStale(layoutIdentifier.Value, expectedVersion, layout.Version));
        }

        var revision = layout.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Option<EditDraftRevisionError>.Some(EditDraftRevisionFailures.LayoutRevisionNotFound(
                layoutIdentifier.Value, revisionNumber.Value));
        }

        return revision.State == LayoutRevisionState.Draft
            ? Option<EditDraftRevisionError>.None
            : Option<EditDraftRevisionError>.Some(EditDraftRevisionFailures.NotADraft(revision.State.Value));
    }
}
