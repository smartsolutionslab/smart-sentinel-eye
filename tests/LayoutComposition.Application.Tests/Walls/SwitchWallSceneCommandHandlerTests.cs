using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Walls;

/// <summary>
/// Spec 258 US1 (T011), plan.md §3. Covers <c>SwitchWallSceneCommandHandler</c>:
/// the happy paths (Layout(x), Next, wrap), the fab-in-lookup + stale-version
/// pattern, <c>WALL_SCENE_NOT_IN_SET</c> (400, US1-11), <c>WALL_SCENE_NOT_PUBLISHED</c>
/// (409, US1-8), and the no-op case (US1-6: publishes nothing, sceneVersion
/// unchanged).
/// </summary>
public class SwitchWallSceneCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private static SwitchWallSceneCommandHandler Handler(
        InMemoryWallRepository walls, FakeLayoutPublicationLookup lookup, FakeClock clock) =>
        new(walls, lookup, clock, NullLogger<SwitchWallSceneCommandHandler>.Instance);

    [Fact]
    public async Task Switching_to_a_named_scene_shows_it()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b, c]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = AllPublishedIn(Munich, a, b, c);
        SwitchWallSceneCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(c), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Showing.ShouldBe(c.Value);
        result.Value.SceneVersion.ShouldBe(1);
    }

    /// <summary>US1-5.</summary>
    [Fact]
    public async Task Next_wraps_from_the_last_scene_back_to_the_first()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b, c]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = AllPublishedIn(Munich, a, b, c);
        SwitchWallSceneCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));
        await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(c), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Next(), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Showing.ShouldBe(a.Value);
    }

    /// <summary>US1-6.</summary>
    [Fact]
    public async Task Switching_to_the_scene_already_showing_is_a_no_op_and_leaves_SceneVersion_unchanged()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        SwitchWallSceneCommandHandler handler = Handler(walls, AllPublishedIn(Munich, a, b), new FakeClock(FixedMoment));

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(a), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SceneVersion.ShouldBe(0);
        wall.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wall_in_another_fab_returns_WALL_NOT_FOUND()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        SwitchWallSceneCommandHandler handler = Handler(walls, AllPublishedIn(Munich, a, b), new FakeClock(FixedMoment));

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Dresden], wall.Id, wall.Version, new SceneTarget.Layout(b), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<SwitchWallSceneError.WallNotFound>();
    }

    [Fact]
    public async Task A_stale_version_on_another_fabs_wall_still_answers_not_found()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        SwitchWallSceneCommandHandler handler = Handler(walls, AllPublishedIn(Munich, a, b), new FakeClock(FixedMoment));

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Dresden], wall.Id, wall.Version + 99, new SceneTarget.Layout(b), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<SwitchWallSceneError.WallNotFound>();
    }

    [Fact]
    public async Task A_stale_expected_version_returns_WALL_STALE()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        SwitchWallSceneCommandHandler handler = Handler(walls, AllPublishedIn(Munich, a, b), new FakeClock(FixedMoment));

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version + 1, new SceneTarget.Layout(b), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_STALE");
    }

    /// <summary>US1-11.</summary>
    [Fact]
    public async Task A_target_not_in_the_scene_set_returns_WALL_SCENE_NOT_IN_SET()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier outside = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        SwitchWallSceneCommandHandler handler = Handler(walls, AllPublishedIn(Munich, a, b), new FakeClock(FixedMoment));

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(outside), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_SCENE_NOT_IN_SET");
    }

    /// <summary>US1-8.</summary>
    [Fact]
    public async Task A_target_scene_no_longer_Published_returns_WALL_SCENE_NOT_PUBLISHED()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich).WithLayout(b, Munich, isPublished: false);
        SwitchWallSceneCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(b), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_SCENE_NOT_PUBLISHED");
    }

    private static FakeLayoutPublicationLookup AllPublishedIn(FabIdentifier fab, params LayoutIdentifier[] scenes)
    {
        FakeLayoutPublicationLookup lookup = new();
        foreach (LayoutIdentifier scene in scenes)
        {
            lookup.WithLayout(scene, fab);
        }
        return lookup;
    }
}
