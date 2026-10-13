using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class CreateWallCommandHandler(
    IWallRepository walls,
    ILayoutPublicationLookup lookup,
    IClock clock,
    ILogger<CreateWallCommandHandler> logger)
    : ICommandHandler<CreateWallCommand, Result<WallIdentifier, CreateWallError>>
{
    public async Task<Result<WallIdentifier, CreateWallError>> HandleAsync(
        CreateWallCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (var fab, var name, var scenes, var by) = command;

        var setViolation = Wall.ValidateScenes(scenes);
        if (setViolation.HasValue)
        {
            return Failure(CreateWallFailures.FromViolation(setViolation.Value, scenes.Count));
        }

        var sceneError = await ValidateScenesAsync(scenes, fab, cancellationToken);
        if (sceneError is not null)
        {
            return Failure(sceneError);
        }

        var existingName = await walls.FindByNameAsync(fab, name, cancellationToken);
        if (existingName.HasValue)
        {
            return Failure(CreateWallFailures.NameTaken(name.Value));
        }

        var wall = Wall.Create(fab, name, scenes, by, clock);
        walls.Add(wall);
        await walls.SaveAsync(cancellationToken);

        logger.CreatedWall(wall.Id, name, by);

        return Success(wall.Id);
    }

    /// <summary>
    /// PD-6 + US1-10: every candidate scene must exist, belong to this
    /// wall's fab, and currently be Published. Checked in that order so a
    /// caller learns "doesn't exist" and "exists elsewhere" apart from
    /// "exists but isn't Published", per plan.md §3's distinct error codes.
    /// </summary>
    private async Task<CreateWallError?> ValidateScenesAsync(
        IReadOnlyList<LayoutIdentifier> scenes, FabIdentifier fab, CancellationToken cancellationToken)
    {
        var fabsOf =
            await lookup.FabsOf(scenes, cancellationToken);

        foreach (var scene in scenes)
        {
            if (!fabsOf.TryGetValue(scene, out var sceneFab))
            {
                return CreateWallFailures.SceneNotFound(scene.Value);
            }
            if (sceneFab != fab)
            {
                return CreateWallFailures.SceneOtherFab(scene.Value);
            }
        }

        var published = await lookup.PublishedAmong(scenes, fab, cancellationToken);
        var unpublished = scenes
            .Where(candidate => !published.Contains(candidate))
            .Cast<LayoutIdentifier?>()
            .FirstOrDefault();

        return unpublished is { } identifier ? CreateWallFailures.SceneNotPublished(identifier.Value) : null;
    }
}
