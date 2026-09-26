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
/// Spec 258 US1 (T011), plan.md §3. Covers <c>EditWallScenesCommandHandler</c>:
/// the fab-in-lookup pattern (mirrors <c>PublishRevisionCommandHandlerTests</c>),
/// <c>WALL_STALE</c>, the scene-set validation codes shared with Create, and
/// US1-15's Reconfigured switch when the edit drops the showing scene.
/// </summary>
public class EditWallScenesCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private static EditWallScenesCommandHandler Handler(
        InMemoryWallRepository walls, FakeLayoutPublicationLookup lookup, FakeClock clock) =>
        new(walls, lookup, clock, NullLogger<EditWallScenesCommandHandler>.Instance);

    [Fact]
    public async Task Editing_the_scene_set_while_Showing_stays_a_member_succeeds_with_no_switch()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        wall.ClearPendingEvents();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich).WithLayout(c, Munich);
        EditWallScenesCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand([Munich], wall.Id, wall.Version, [a, c], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Showing.ShouldBe(a.Value);
        result.Value.Scenes.ShouldBe([a.Value, c.Value]);
    }

    /// <summary>US1-15.</summary>
    [Fact]
    public async Task Dropping_the_Showing_scene_moves_the_pointer_to_the_new_first_scene()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b, c]).At(FixedMoment).Build();
        wall.SwitchTo(new SceneTarget.Layout(b), new HashSet<LayoutIdentifier> { a, b, c },
            new SceneSwitchCause.Operator(OperatorIdentifier.From(Guid.CreateVersion7())), new FakeClock(FixedMoment));
        wall.ClearPendingEvents();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich).WithLayout(c, Munich);
        EditWallScenesCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand([Munich], wall.Id, wall.Version, [a, c], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Showing.ShouldBe(a.Value);
        wall.Showing.ShouldBe(a);
    }

    [Fact]
    public async Task A_wall_in_another_fab_returns_WALL_NOT_FOUND()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup().WithLayout(c, Munich);
        EditWallScenesCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand([Dresden], wall.Id, wall.Version, [a, c], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_NOT_FOUND");
    }

    /// <summary>Mirrors PublishRevisionCommandHandlerTests: a stale version on another fab's wall still answers not-found.</summary>
    [Fact]
    public async Task A_stale_version_on_another_fabs_wall_still_answers_not_found()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        EditWallScenesCommandHandler handler = Handler(walls, new FakeLayoutPublicationLookup(), new FakeClock(FixedMoment));

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand([Dresden], wall.Id, wall.Version + 99, [a, b], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditWallScenesError.WallNotFound>();
    }

    [Fact]
    public async Task A_stale_expected_version_returns_WALL_STALE()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich).WithLayout(c, Munich);
        EditWallScenesCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand([Munich], wall.Id, wall.Version + 1, [a, c], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_STALE");
    }

    [Fact]
    public async Task Editing_down_to_a_single_scene_returns_WALL_TOO_FEW_SCENES()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        EditWallScenesCommandHandler handler = Handler(walls, new FakeLayoutPublicationLookup().WithLayout(a, Munich), new FakeClock(FixedMoment));

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand([Munich], wall.Id, wall.Version, [a], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_TOO_FEW_SCENES");
    }

    [Fact]
    public async Task A_new_scene_from_another_fab_returns_WALL_SCENE_OTHER_FAB()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier otherFabLayout = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich).WithLayout(otherFabLayout, Dresden);
        EditWallScenesCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand([Munich], wall.Id, wall.Version, [a, otherFabLayout], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_SCENE_OTHER_FAB");
    }

    [Fact]
    public async Task A_new_scene_with_no_Published_revision_returns_WALL_SCENE_NOT_PUBLISHED()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier draftOnly = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich).WithLayout(draftOnly, Munich, isPublished: false);
        EditWallScenesCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand([Munich], wall.Id, wall.Version, [a, draftOnly], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_SCENE_NOT_PUBLISHED");
    }
}
