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

        (var fabs, var wallIdentifier, var expectedVersion,
            var newScenes, var by) = command;

        var found = await walls.FindAsync(wallIdentifier, fabs, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(EditWallScenesFailures.WallNotFound(wallIdentifier.Value));
        }

        var wall = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the wall that
        // has since moved. Checked before any mutation.
        if (wall.Version != expectedVersion)
        {
            return Failure(EditWallScenesFailures.Stale(wallIdentifier.Value, expectedVersion, wall.Version));
        }

        var setViolation = Wall.ValidateScenes(newScenes);
        if (setViolation.HasValue)
        {
            return Failure(EditWallScenesFailures.FromViolation(setViolation.Value, newScenes.Count));
        }

        var sceneError = await ValidateScenesAsync(newScenes, wall.Fab, cancellationToken);
        if (sceneError is not null)
        {
            return Failure(sceneError);
        }

        wall.EditScenes(newScenes, by, clock);
        await walls.SaveAsync(cancellationToken);

        logger.EditedWallScenes(wall.Id, by);

        return Success(wall.ToDto());
    }

    /// <summary>Mirrors <c>CreateWallCommandHandler</c>'s three-step scene validation.</summary>
    private async Task<EditWallScenesError?> ValidateScenesAsync(
        IReadOnlyList<LayoutIdentifier> scenes, FabIdentifier fab, CancellationToken cancellationToken)
    {
        var fabsOf =
            await lookup.FabsOf(scenes, cancellationToken);

        foreach (var scene in scenes)
        {
            if (!fabsOf.TryGetValue(scene, out var sceneFab))
            {
                return EditWallScenesFailures.SceneNotFound(scene.Value);
            }
            if (sceneFab != fab)
            {
                return EditWallScenesFailures.SceneOtherFab(scene.Value);
            }
        }

        var published = await lookup.PublishedAmong(scenes, fab, cancellationToken);
        var unpublished = scenes
            .Where(candidate => !published.Contains(candidate))
            .Cast<LayoutIdentifier?>()
            .FirstOrDefault();

        return unpublished is { } identifier ? EditWallScenesFailures.SceneNotPublished(identifier.Value) : null;
    }
}
