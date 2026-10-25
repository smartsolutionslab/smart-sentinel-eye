using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Walls;

// Spec 459 (#3154), phase 4a: every WallDto field on the mapping as it stands. The wall is
// created at one moment and switched at a later one, so Fab, ShowingSince and SceneVersion
// cannot be satisfied by a sibling's value.
public class WallDtoMappingCharacterisationTests
{
    [Fact]
    public async Task A_switched_wall_maps_every_field_from_its_own_source()
    {
        var created = DateTimeOffset.Parse("2026-07-01T08:00:00Z", CultureInfo.InvariantCulture);
        var switched = DateTimeOffset.Parse("2026-07-01T09:45:00Z", CultureInfo.InvariantCulture);
        var hamburg = FabIdentifier.From("hamburg");
        var first = LayoutIdentifier.New();
        var second = LayoutIdentifier.New();
        var third = LayoutIdentifier.New();
        var wall = new WallBuilder()
            .WithFab(hamburg)
            .Named("Control room")
            .WithScenes([first, second, third])
            .At(created)
            .Build();
        wall.SwitchTo(
            new SceneTarget.Layout(third),
            new HashSet<LayoutIdentifier> { first, second, third },
            new SceneSwitchCause.Operator(OperatorIdentifier.From(Guid.CreateVersion7())),
            new LayoutBuilder.TestClock(switched));
        InMemoryWallRepository walls = new();
        walls.Add(wall);

        var result = await new GetWallQueryHandler(walls)
            .HandleAsync(new GetWallQuery([hamburg], wall.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.Wall.ShouldBe(wall.Id.Value);
        dto.Version.ShouldBe(wall.Version);
        dto.Fab.ShouldBe("hamburg");
        dto.Name.ShouldBe("Control room");
        dto.Scenes.ShouldBe([first.Value, second.Value, third.Value]);
        dto.Showing.ShouldBe(third.Value);
        dto.SceneVersion.ShouldBe(1);
        dto.ShowingSince.ShouldBe(switched);
    }

    [Fact]
    public async Task A_fresh_wall_shows_since_its_creation_moment()
    {
        var created = DateTimeOffset.Parse("2026-07-01T08:00:00Z", CultureInfo.InvariantCulture);
        var hamburg = FabIdentifier.From("hamburg");
        var wall = new WallBuilder().WithFab(hamburg).At(created).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);

        var result = await new GetWallQueryHandler(walls)
            .HandleAsync(new GetWallQuery([hamburg], wall.Id), CancellationToken.None);

        result.Value.ShowingSince.ShouldBe(created);
        result.Value.SceneVersion.ShouldBe(0);
    }
}
