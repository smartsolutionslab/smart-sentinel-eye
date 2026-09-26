using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Creates a new <see cref="Wall"/> (spec 258 US1, FR-001..003). Every scene
/// must be a Published layout in <paramref name="Fab"/> (PD-6); the name
/// must be unique among live walls in that fab.
/// </summary>
public sealed record CreateWallCommand(
    FabIdentifier Fab,
    WallName Name,
    IReadOnlyList<LayoutIdentifier> Scenes,
    OperatorIdentifier By)
    : ICommand<Result<WallIdentifier, CreateWallError>>;
