using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Replaces a Draft revision's grid + tile set in place (spec 010). A
/// multi-tile edit swaps the whole set atomically, so there is no
/// per-tile or tri-state overlay input — the command carries the new
/// grid + tiles and the aggregate replaces the revision's payload. The
/// grid + tiles must satisfy the four grid invariants (ADR-0112 §2).
/// </summary>
public sealed record EditDraftRevisionCommand(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    GridDimensions Grid,
    IReadOnlyList<Tile> Tiles,
    int ExpectedVersion)
    : ICommand<Result<LayoutRevisionNumber, EditDraftRevisionError>>;
