using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class CreateLayoutDraftCommandHandler(
    ILayoutRepository layouts,
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
