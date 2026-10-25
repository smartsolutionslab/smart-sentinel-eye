using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

/// <summary>
/// A manual switch (spec 258 US1, PD-4). "Not in the scene set" and "not
/// currently Published" are checked here, before
/// <see cref="Wall.SwitchTo"/> — that method throws for both as a
/// programmer-error backstop, and the operator-facing answers are 400 and
/// 409 respectively (plan.md §3).
/// </summary>
public sealed class SwitchWallSceneCommandHandler(
    IWallRepository walls,
    ILayoutPublicationLookup lookup,
    IClock clock,
    ILogger<SwitchWallSceneCommandHandler> logger)
    : ICommandHandler<SwitchWallSceneCommand, Result<WallDto, SwitchWallSceneError>>
{
    public async Task<Result<WallDto, SwitchWallSceneError>> HandleAsync(
        SwitchWallSceneCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (var fabs, var wallIdentifier, var expectedVersion, var target, var by) = command;

        var found = await walls.FindAsync(wallIdentifier, fabs, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(SwitchWallSceneFailures.WallNotFound(wallIdentifier.Value));
        }

        var wall = found.Value;

        LayoutIdentifier? namedLayout = target is SceneTarget.Layout layout ? layout.Value : null;
        var refusal = RefuseBeforeLookup(wall, wallIdentifier, expectedVersion, namedLayout);
        if (refusal.HasValue)
        {
            return Failure(refusal.Value);
        }

        var alreadyShowing = await CompleteIfAlreadyShowingAsync(wall, namedLayout, by, cancellationToken);
        if (alreadyShowing.HasValue)
        {
            return Success(alreadyShowing.Value);
        }

        var publishable =
            await lookup.PublishedAmong(wall.Scenes, wall.Fab, cancellationToken);

        if (namedLayout is { } wanted && !publishable.Contains(wanted))
        {
            return Failure(SwitchWallSceneFailures.SceneNotPublished(wanted.Value));
        }

        var switched =
            wall.SwitchTo(target, publishable, new SceneSwitchCause.Operator(by), clock);
        await walls.SaveAsync(cancellationToken);

        LogOutcome(wall, switched.HasValue, by);

        return Success(wall.ToDto());
    }

    private static Option<SwitchWallSceneError> RefuseBeforeLookup(
        Wall wall,
        WallIdentifier wallIdentifier,
        int expectedVersion,
        LayoutIdentifier? namedLayout)
    {
        if (wall.Version != expectedVersion)
        {
            return Option<SwitchWallSceneError>.Some(
                SwitchWallSceneFailures.Stale(wallIdentifier.Value, expectedVersion, wall.Version));
        }

        return namedLayout is { } requested && !wall.Scenes.Contains(requested)
            ? Option<SwitchWallSceneError>.Some(SwitchWallSceneFailures.SceneNotInSet(requested.Value))
            : Option<SwitchWallSceneError>.None;
    }

    private async Task<Option<WallDto>> CompleteIfAlreadyShowingAsync(
        Wall wall,
        LayoutIdentifier? namedLayout,
        OperatorIdentifier by,
        CancellationToken cancellationToken)
    {
        if (namedLayout is not { } already || already != wall.Showing)
        {
            return Option<WallDto>.None;
        }

        // Already showing it: the same no-op success `Wall.SwitchTo` guarantees
        // (Wall.cs's own Showing precedence) — whether the target is still
        // Published is irrelevant to a switch that doesn't move `Showing` at
        // all, so this must not depend on `publishable` in the caller.
        await walls.SaveAsync(cancellationToken);
        logger.WallSceneSwitchWasNoOp(wall.Id, by);
        return Option<WallDto>.Some(wall.ToDto());
    }

    private void LogOutcome(Wall wall, bool switched, OperatorIdentifier by)
    {
        if (switched)
        {
            logger.SwitchedWallScene(wall.Id, by);
        }
        else
        {
            logger.WallSceneSwitchWasNoOp(wall.Id, by);
        }
    }
}
