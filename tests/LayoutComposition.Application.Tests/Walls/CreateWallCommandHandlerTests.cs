using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Walls;

/// <summary>
/// Spec 258 US1 (T011), plan.md §3. Covers <c>CreateWallCommandHandler</c>:
/// every <c>WALL_*</c> 400/409 error code from plan.md §3's table, the
/// fab-scoped scene checks (FR-001), and PD-6's "every referenced layout must
/// be Published at create time" rule.
/// </summary>
public class CreateWallCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private static CreateWallCommandHandler Handler(
        InMemoryWallRepository walls, FakeLayoutPublicationLookup lookup, FakeClock clock) =>
        new(walls, lookup, clock, NullLogger<CreateWallCommandHandler>.Instance);

    private static FakeLayoutPublicationLookup PublishedIn(FabIdentifier fab, params LayoutIdentifier[] scenes)
    {
        FakeLayoutPublicationLookup lookup = new();
        foreach (LayoutIdentifier scene in scenes)
        {
            lookup.WithLayout(scene, fab);
        }
        return lookup;
    }

    [Fact]
    public async Task Creating_with_two_Published_scenes_in_the_same_fab_succeeds()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        InMemoryWallRepository walls = new();
        FakeLayoutPublicationLookup lookup = PublishedIn(Munich, a, b);
        CreateWallCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), [a, b], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        walls.Walls.ShouldHaveSingleItem();
        Wall created = walls.Walls[0];
        created.Id.ShouldBe(result.Value);
        created.Showing.ShouldBe(a);
        created.Scenes.ShouldBe([a, b]);
    }

    [Fact]
    public async Task A_single_scene_returns_WALL_TOO_FEW_SCENES()
    {
        LayoutIdentifier a = NewScene();
        InMemoryWallRepository walls = new();
        CreateWallCommandHandler handler = Handler(walls, PublishedIn(Munich, a), new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), [a], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_TOO_FEW_SCENES");
        walls.Walls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nine_distinct_scenes_returns_WALL_TOO_MANY_SCENES()
    {
        IReadOnlyList<LayoutIdentifier> nine = [.. Enumerable.Range(0, 9).Select(_ => NewScene())];
        InMemoryWallRepository walls = new();
        FakeLayoutPublicationLookup lookup = new();
        foreach (LayoutIdentifier scene in nine)
        {
            lookup.WithLayout(scene, Munich);
        }
        CreateWallCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), nine, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_TOO_MANY_SCENES");
    }

    [Fact]
    public async Task A_duplicate_scene_returns_WALL_DUPLICATE_SCENE()
    {
        LayoutIdentifier a = NewScene();
        InMemoryWallRepository walls = new();
        CreateWallCommandHandler handler = Handler(walls, PublishedIn(Munich, a), new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), [a, a], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_DUPLICATE_SCENE");
    }

    [Fact]
    public async Task A_scene_layout_belonging_to_another_fab_returns_WALL_SCENE_OTHER_FAB()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier otherFabLayout = NewScene();
        InMemoryWallRepository walls = new();
        FakeLayoutPublicationLookup lookup = PublishedIn(Munich, a).WithLayout(otherFabLayout, Dresden);
        CreateWallCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), [a, otherFabLayout], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_SCENE_OTHER_FAB");
        walls.Walls.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_scene_layout_returns_WALL_SCENE_NOT_FOUND()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier unknown = NewScene();
        InMemoryWallRepository walls = new();
        CreateWallCommandHandler handler = Handler(walls, PublishedIn(Munich, a), new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), [a, unknown], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_SCENE_NOT_FOUND");
        walls.Walls.ShouldBeEmpty();
    }

    /// <summary>PD-6: "Configuring a wall requires every referenced layout to be Published at create... time."</summary>
    [Fact]
    public async Task A_scene_layout_with_no_Published_revision_returns_WALL_SCENE_NOT_PUBLISHED()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier draftOnly = NewScene();
        InMemoryWallRepository walls = new();
        FakeLayoutPublicationLookup lookup = PublishedIn(Munich, a).WithLayout(draftOnly, Munich, isPublished: false);
        CreateWallCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), [a, draftOnly], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_SCENE_NOT_PUBLISHED");
        walls.Walls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_duplicate_name_within_the_same_fab_returns_WALL_NAME_TAKEN()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        InMemoryWallRepository walls = new();
        walls.Add(new WallBuilder().WithFab(Munich).Named("Line 3 rotation").At(FixedMoment).Build());
        CreateWallCommandHandler handler = Handler(walls, PublishedIn(Munich, a, b), new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), [a, b], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WALL_NAME_TAKEN");
        walls.Walls.ShouldHaveSingleItem();
    }

    /// <summary>FR-019's twin for walls: a name held in another fab does not block this one.</summary>
    [Fact]
    public async Task The_same_name_is_accepted_in_a_different_fab()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        InMemoryWallRepository walls = new();
        walls.Add(new WallBuilder().WithFab(Dresden).Named("Line 3 rotation").At(FixedMoment).Build());
        CreateWallCommandHandler handler = Handler(walls, PublishedIn(Munich, a, b), new FakeClock(FixedMoment));

        Result<WallIdentifier, CreateWallError> result = await handler.HandleAsync(
            new CreateWallCommand(Munich, WallName.From("Line 3 rotation"), [a, b], OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        walls.Walls.Count.ShouldBe(2);
    }
}
