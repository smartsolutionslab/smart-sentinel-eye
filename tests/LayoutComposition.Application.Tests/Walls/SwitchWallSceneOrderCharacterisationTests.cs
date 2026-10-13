using System.Globalization;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Walls;

/// <summary>
/// Characterisation (spec 345, ADR-0144): pins the order in which the manual
/// switch refuses when more than one refusal applies, and the exact text and
/// level of the outcome log for a real switch, an already-showing no-op and a
/// <c>Next</c> that finds nothing else Published. The existing tests cover each
/// refusal alone and assert no log at all.
/// </summary>
public class SwitchWallSceneOrderCharacterisationTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private static OperatorIdentifier NewOperator() => OperatorIdentifier.From(Guid.CreateVersion7());

    private static SwitchWallSceneCommandHandler Handler(
        InMemoryWallRepository walls,
        FakeLayoutPublicationLookup lookup,
        CapturingLogger<SwitchWallSceneCommandHandler> logger) =>
        new(walls, lookup, new FakeClock(FixedMoment), logger);

    private static FakeLayoutPublicationLookup AllPublishedIn(FabIdentifier fab, params LayoutIdentifier[] scenes)
    {
        FakeLayoutPublicationLookup lookup = new();
        foreach (var scene in scenes)
        {
            lookup.WithLayout(scene, fab);
        }

        return lookup;
    }

    [Fact]
    public async Task A_stale_version_and_a_target_outside_the_scene_set_returns_WALL_STALE()
    {
        var a = NewScene();
        var b = NewScene();
        var wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<SwitchWallSceneCommandHandler> logger = new();
        var handler = Handler(walls, AllPublishedIn(Munich, a, b), logger);

        var result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version + 1, new SceneTarget.Layout(NewScene()), NewOperator()),
            CancellationToken.None);

        result.Error.Code.ShouldBe("WALL_STALE");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stale_version_and_the_scene_already_showing_returns_WALL_STALE()
    {
        var a = NewScene();
        var b = NewScene();
        var wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<SwitchWallSceneCommandHandler> logger = new();
        var handler = Handler(walls, AllPublishedIn(Munich, a, b), logger);

        var result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version + 1, new SceneTarget.Layout(a), NewOperator()),
            CancellationToken.None);

        result.Error.Code.ShouldBe("WALL_STALE");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_target_outside_the_scene_set_that_is_also_unpublished_returns_WALL_SCENE_NOT_IN_SET()
    {
        var a = NewScene();
        var b = NewScene();
        var outside = NewScene();
        var wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        var lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich)
            .WithLayout(b, Munich)
            .WithLayout(outside, Munich, isPublished: false);
        var handler = Handler(walls, lookup, new CapturingLogger<SwitchWallSceneCommandHandler>());

        var result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(outside), NewOperator()),
            CancellationToken.None);

        result.Error.Code.ShouldBe("WALL_SCENE_NOT_IN_SET");
    }

    [Fact]
    public async Task A_real_switch_logs_the_wall_and_operator_at_Information()
    {
        var a = NewScene();
        var b = NewScene();
        var wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<SwitchWallSceneCommandHandler> logger = new();
        var handler = Handler(walls, AllPublishedIn(Munich, a, b), logger);
        var by = NewOperator();

        var result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(b), by),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Information);
        logger.Entries[0].Message.ShouldBe($"Switched wall {wall.Id} scene by {by}.");
    }

    [Fact]
    public async Task Switching_to_the_scene_already_showing_logs_a_no_op_at_Debug()
    {
        var a = NewScene();
        var b = NewScene();
        var wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<SwitchWallSceneCommandHandler> logger = new();
        var handler = Handler(walls, AllPublishedIn(Munich, a, b), logger);
        var by = NewOperator();

        var result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(a), by),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Debug);
        logger.Entries[0].Message.ShouldBe($"Switch on wall {wall.Id} by {by} was a no-op; scene unchanged.");
    }

    [Fact]
    public async Task Next_with_no_other_Published_scene_logs_a_no_op_at_Debug_and_leaves_the_scene_unchanged()
    {
        var a = NewScene();
        var b = NewScene();
        var wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        var lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich)
            .WithLayout(b, Munich, isPublished: false);
        CapturingLogger<SwitchWallSceneCommandHandler> logger = new();
        var handler = Handler(walls, lookup, logger);
        var by = NewOperator();

        var result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Next(), by),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Showing.ShouldBe(a.Value);
        result.Value.SceneVersion.ShouldBe(0);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Debug);
        logger.Entries[0].Message.ShouldBe($"Switch on wall {wall.Id} by {by} was a no-op; scene unchanged.");
    }

    [Fact]
    public async Task Next_with_another_Published_scene_switches_to_it_and_logs_the_switch()
    {
        var a = NewScene();
        var b = NewScene();
        var wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<SwitchWallSceneCommandHandler> logger = new();
        var handler = Handler(walls, AllPublishedIn(Munich, a, b), logger);
        var by = NewOperator();

        var result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Next(), by),
            CancellationToken.None);

        result.Value.Showing.ShouldBe(b.Value);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Information);
        logger.Entries[0].Message.ShouldBe($"Switched wall {wall.Id} scene by {by}.");
    }
}
