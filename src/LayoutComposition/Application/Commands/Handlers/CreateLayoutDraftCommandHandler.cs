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
        (LayoutName? name, GridDimensions grid, IReadOnlyList<Tile> tiles, OperatorIdentifier createdBy) = command;

        Option<GridViolation> violation = Layout.ValidateGrid(grid, tiles);
        if (violation.HasValue)
        {
            return Result<LayoutIdentifier, CreateLayoutDraftError>.Failure(
                CreateLayoutDraftError.FromViolation(violation.Value));
        }

        Option<Layout> existing = await layouts
            .GetByNameAsync(name, cancellationToken);
        if (existing.HasValue)
        {
            return Result<LayoutIdentifier, CreateLayoutDraftError>.Failure(
                new CreateLayoutDraftError.LayoutNameTaken(name.Value));
        }

        Layout layout = Layout.CreateDraft(name, grid, tiles, createdBy, clock);
        layouts.Add(layout);
        await layouts.SaveAsync(cancellationToken);

        logger.CreatedLayout(layout.Id, name, createdBy);

        return Result<LayoutIdentifier, CreateLayoutDraftError>.Success(layout.Id);
    }
}
