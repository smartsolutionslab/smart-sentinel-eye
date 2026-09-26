using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Walls;

/// <summary>Spec 258 US1 (T011), plan.md §3. Mirrors ListLayoutsQueryHandlerTests' fab-scoping conventions.</summary>
public class ListWallsQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    [Fact]
    public async Task Lists_every_wall_in_the_callers_fabs()
    {
        InMemoryWallRepository walls = new();
        walls.Add(new WallBuilder().WithFab(Munich).Named("In-Munich").At(FixedMoment).Build());
        walls.Add(new WallBuilder().WithFab(Dresden).Named("In-Dresden").At(FixedMoment).Build());
        ListWallsQueryHandler handler = new(walls);

        IReadOnlyList<WallDto> result = await handler.HandleAsync(
            new ListWallsQuery([Munich, Dresden]), CancellationToken.None);

        result.Select(dto => dto.Name).ShouldBe(["In-Munich", "In-Dresden"], ignoreOrder: true);
    }

    /// <summary>FR-005's read-side pattern, reused for walls.</summary>
    [Fact]
    public async Task Omits_a_wall_in_a_fab_the_caller_does_not_hold()
    {
        InMemoryWallRepository walls = new();
        walls.Add(new WallBuilder().WithFab(Munich).Named("In-Munich").At(FixedMoment).Build());
        walls.Add(new WallBuilder().WithFab(Dresden).Named("In-Dresden").At(FixedMoment).Build());
        ListWallsQueryHandler handler = new(walls);

        IReadOnlyList<WallDto> result = await handler.HandleAsync(
            new ListWallsQuery([Dresden]), CancellationToken.None);

        result.Select(dto => dto.Name).ShouldBe(["In-Dresden"]);
    }
}
