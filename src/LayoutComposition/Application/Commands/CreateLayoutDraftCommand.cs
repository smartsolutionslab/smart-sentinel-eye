using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Creates the first revision of a new logical Layout chain in Draft
/// state (spec 003 FR-006, spec 010). Name must be unique across all
/// non-Archived chains; the grid + tile set must satisfy the four
/// grid invariants (ADR-0112 §2).
/// </summary>
public sealed record CreateLayoutDraftCommand(
    FabIdentifier Fab,
    LayoutName Name,
    GridDimensions Grid,
    IReadOnlyList<Tile> Tiles,
    OperatorIdentifier CreatedBy)
    : ICommand<Result<LayoutIdentifier, CreateLayoutDraftError>>;
