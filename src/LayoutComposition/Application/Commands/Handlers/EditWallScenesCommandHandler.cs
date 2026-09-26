using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class EditWallScenesCommandHandler(
    IWallRepository walls,
    ILayoutPublicationLookup lookup,
    IClock clock,
    ILogger<EditWallScenesCommandHandler> logger)
    : ICommandHandler<EditWallScenesCommand, Result<WallDto, EditWallScenesError>>
{
    public async Task<Result<WallDto, EditWallScenesError>> HandleAsync(
        EditWallScenesCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (IReadOnlyList<FabIdentifier> fabs, WallIdentifier wallIdentifier, int expectedVersion,
            IReadOnlyList<LayoutIdentifier> newScenes, OperatorIdentifier by) = command;

        Option<Wall> found = await walls.FindAsync(wallIdentifier, fabs, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(EditWallScenesFailures.WallNotFound(wallIdentifier.Value));
        }

        Wall wall = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the wall that
        // has since moved. Checked before any mutation.
        if (wall.Version != expectedVersion)
        {
            return Failure(EditWallScenesFailures.Stale(wallIdentifier.Value, expectedVersion, wall.Version));
        }

        Option<SceneSetViolation> setViolation = Wall.ValidateScenes(newScenes);
        if (setViolation.HasValue)
        {
            return Failure(EditWallScenesFailures.FromViolation(setViolation.Value, newScenes.Count));
        }

        EditWallScenesError? sceneError = await ValidateScenesAsync(newScenes, wall.Fab, cancellationToken);
        if (sceneError is not null)
        {
            return Failure(sceneError);
        }

        wall.EditScenes(newScenes, by, clock);
        await walls.SaveAsync(cancellationToken);

        logger.EditedWallScenes(wall.Id, by);

        return Success(GetWallQueryHandler.Map(wall));
    }

    /// <summary>
    /// Mirrors <c>CreateWallCommandHandler</c>'s scene validation: a layout
    /// that exists only in a different fab does not count as existing at
    /// all, so it gets the same <c>WALL_SCENE_NOT_FOUND</c> as one that
    /// doesn't exist anywhere — never a distinct code that would disclose
    /// the identifier exists somewhere else.
    /// </summary>
    private async Task<EditWallScenesError?> ValidateScenesAsync(
        IReadOnlyList<LayoutIdentifier> scenes, FabIdentifier fab, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<LayoutIdentifier, FabIdentifier> fabsOf =
            await lookup.FabsOf(scenes, cancellationToken);

        foreach (LayoutIdentifier scene in scenes)
        {
            if (!fabsOf.TryGetValue(scene, out FabIdentifier? sceneFab) || sceneFab != fab)
            {
                return EditWallScenesFailures.SceneNotFound(scene.Value);
            }
        }

        IReadOnlySet<LayoutIdentifier> published = await lookup.PublishedAmong(scenes, fab, cancellationToken);
        LayoutIdentifier? unpublished = scenes
            .Where(candidate => !published.Contains(candidate))
            .Cast<LayoutIdentifier?>()
            .FirstOrDefault();

        return unpublished is { } identifier ? EditWallScenesFailures.SceneNotPublished(identifier.Value) : null;
    }
}
