using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Walls;

/// <summary>Spec 258 US1 (T011), plan.md §3. US1-14: another fab's wall is 404, never 403.</summary>
public class GetWallQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    [Fact]
    public async Task An_existing_wall_is_mapped_into_a_WallDto()
    {
        LayoutIdentifier a = LayoutIdentifier.New();
        LayoutIdentifier b = LayoutIdentifier.New();
        Wall wall = new WallBuilder().WithFab(Munich).Named("Line 3 rotation").WithScenes([a, b]).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        GetWallQueryHandler handler = new(walls);

        Result<WallDto, GetWallError> result = await handler.HandleAsync(
            new GetWallQuery([Munich], wall.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        WallDto dto = result.Value;
        dto.Wall.ShouldBe(wall.Id.Value);
        dto.Name.ShouldBe("Line 3 rotation");
        dto.Scenes.ShouldBe([a.Value, b.Value]);
        dto.Showing.ShouldBe(a.Value);
        dto.SceneVersion.ShouldBe(0);
        dto.Version.ShouldBe(wall.Version);
    }

    [Fact]
    public async Task An_unknown_wall_returns_WALL_NOT_FOUND()
    {
        InMemoryWallRepository walls = new();
        GetWallQueryHandler handler = new(walls);

        Result<WallDto, GetWallError> result = await handler.HandleAsync(
            new GetWallQuery([Munich], WallIdentifier.New()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetWallError.WallNotFound>();
    }

    /// <summary>US1-14: not disclosed as forbidden — the same answer as an unknown identifier.</summary>
    [Fact]
    public async Task A_wall_in_a_fab_the_caller_does_not_hold_is_reported_as_not_found()
    {
        Wall inMunich = new WallBuilder().WithFab(Munich).At(FixedMoment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(inMunich);
        GetWallQueryHandler handler = new(walls);

        Result<WallDto, GetWallError> result = await handler.HandleAsync(
            new GetWallQuery([Dresden], inMunich.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetWallError.WallNotFound>();
    }
}
