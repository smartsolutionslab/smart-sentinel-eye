using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Application.Tiles;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class CreateLayoutDraftCommandHandler(
    ILayoutRepository layouts,
    ICameraFabGuard cameraFabs,
    IClock clock,
    ILogger<CreateLayoutDraftCommandHandler> logger)
    : ICommandHandler<CreateLayoutDraftCommand, Result<LayoutIdentifier, CreateLayoutDraftError>>
{
    public async Task<Result<LayoutIdentifier, CreateLayoutDraftError>> HandleAsync(
        CreateLayoutDraftCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (FabIdentifier fab, LayoutName? name, GridDimensions grid, IReadOnlyList<Tile> tiles, OperatorIdentifier createdBy) = command;

        Option<GridViolation> violation = Layout.ValidateGrid(grid, tiles);
        if (violation.HasValue)
        {
            return Failure(CreateLayoutDraftError.FromViolation(violation.Value));
        }

        // FR-014: every tile's camera must be in this layout's fab. Checked
        // before the name lookup because a cross-fab tile is a boundary
        // violation, and a name collision is not.
        IReadOnlyList<CameraIdentifier> outside = await cameraFabs
            .CamerasOutsideFabAsync(fab, [.. tiles.Select(tile => tile.Camera)], cancellationToken);
        if (outside.Count > 0)
        {
            logger.RefusedCrossFabTiles(fab, outside.Count);
            return Failure(CreateLayoutDraftFailures.TileCameraOutsideFab(
                fab.Value, [.. outside.Select(camera => camera.Value)]));
        }

        // Scoped to the fab (FR-019). A global check would answer
        // LAYOUT_NAME_TAKEN for a layout in another plant, which both blocks a
        // legitimate name and confirms that the other layout exists.
        Option<Layout> existing = await layouts
            .GetByNameAsync(fab, name, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(CreateLayoutDraftFailures.LayoutNameTaken(name.Value));
        }

        Layout layout = Layout.CreateDraft(fab, name, grid, tiles, createdBy, clock);
        layouts.Add(layout);
        await layouts.SaveAsync(cancellationToken);

        logger.CreatedLayout(layout.Id, name, createdBy);

        return Success(layout.Id);
    }
}
