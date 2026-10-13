using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Queries;

/// <summary>
/// Characterisation (spec 345, ADR-0144): pins the order of both listing
/// shapes and the field mapping of the kiosk picker row. The existing tests
/// count and filter; none of them looks at order, and the order is what a
/// split of the handler into two shape methods could silently change.
/// </summary>
public class ListLayoutsOrderingCharacterisationTests
{
    private static readonly DateTimeOffset CreatedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset PublishedMoment =
        DateTimeOffset.Parse("2026-06-01T08:15:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static Layout PublishedChain(string name)
    {
        var builder = new LayoutBuilder().Named(name).At(CreatedMoment);
        var layout = builder.Build();
        layout.Publish(LayoutRevisionNumber.One, builder.Operator, builder.Clock);
        return layout;
    }

    private static async Task<ListLayoutsResult> ListAsync(
        InMemoryLayoutRepository repository,
        LayoutRevisionState? state)
    {
        ListLayoutsQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        var result = await handler.HandleAsync(new ListLayoutsQuery([Munich], state), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact]
    public async Task The_picker_is_ordered_by_name_ignoring_case()
    {
        InMemoryLayoutRepository repository = new();
        repository.Add(PublishedChain("b"));
        repository.Add(PublishedChain("C"));
        repository.Add(PublishedChain("a"));

        var listing = await ListAsync(repository, LayoutRevisionState.Published);

        listing.Published.Select(dto => dto.Name).ShouldBe(["a", "b", "C"]);
    }

    [Fact]
    public async Task The_picker_orders_names_ordinally_not_by_culture()
    {
        InMemoryLayoutRepository repository = new();
        repository.Add(PublishedChain("Cam_1"));
        repository.Add(PublishedChain("CamA"));
        repository.Add(PublishedChain("Cam-1"));

        var listing = await ListAsync(repository, LayoutRevisionState.Published);

        listing.Published.Select(dto => dto.Name).ShouldBe(["Cam-1", "CamA", "Cam_1"]);
    }

    [Fact]
    public async Task A_picker_row_carries_the_published_revisions_number_grid_tiles_and_publish_time()
    {
        var camera = CameraIdentifier.From(Guid.CreateVersion7());
        var overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        var builder = new LayoutBuilder()
            .Named("Line-1")
            .WithGrid(GridDimensions.From(2, 3))
            .WithTiles([new Tile(camera, Option<OverlayIdentifier>.Some(overlay), GridPosition.From(1, 2))])
            .At(CreatedMoment);
        var layout = builder.Build();
        layout.Publish(LayoutRevisionNumber.One, builder.Operator, builder.Clock);
        var branched = layout.BranchDraft(builder.Operator, new LayoutBuilder.TestClock(PublishedMoment));
        layout.Publish(branched.Number, builder.Operator, new LayoutBuilder.TestClock(PublishedMoment));
        InMemoryLayoutRepository repository = new();
        repository.Add(layout);

        var listing = await ListAsync(repository, LayoutRevisionState.Published);

        var row = listing.Published.ShouldHaveSingleItem();
        row.LayoutIdentifier.ShouldBe(layout.Id.Value);
        row.Name.ShouldBe("Line-1");
        row.RevisionNumber.ShouldBe(2);
        row.GridRows.ShouldBe(2);
        row.GridCols.ShouldBe(3);
        row.PublishedAt.ShouldBe(PublishedMoment);
        var tile = row.Tiles.ShouldHaveSingleItem();
        tile.CameraIdentifier.ShouldBe(camera.Value);
        tile.OverlayIdentifier.ShouldBe(overlay.Value);
        tile.Row.ShouldBe(1);
        tile.Col.ShouldBe(2);
    }

    [Fact]
    public async Task The_admin_list_is_ordered_newest_created_first()
    {
        InMemoryLayoutRepository repository = new();
        repository.Add(new LayoutBuilder().Named("middle").At(CreatedMoment.AddHours(1)).Build());
        repository.Add(new LayoutBuilder().Named("newest").At(CreatedMoment.AddHours(2)).Build());
        repository.Add(new LayoutBuilder().Named("oldest").At(CreatedMoment).Build());

        var listing = await ListAsync(repository, state: null);

        listing.Chains.Select(dto => dto.Name).ShouldBe(["newest", "middle", "oldest"]);
        listing.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_admin_list_carries_every_revision_of_each_chain()
    {
        var chain = PublishedChain("Line-1");
        var branched = chain.BranchDraft(
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(PublishedMoment));
        InMemoryLayoutRepository repository = new();
        repository.Add(chain);

        var listing = await ListAsync(repository, state: null);

        var dto = listing.Chains.ShouldHaveSingleItem();
        dto.Revisions.Select(revision => revision.RevisionNumber).ShouldBe([1, branched.Number.Value], ignoreOrder: true);
    }
}
