using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using LayoutAggregate = SmartSentinelEye.LayoutComposition.Domain.Layout.Layout;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Queries;

// Spec 459 (#3154), phase 4a: every LayoutDto / LayoutRevisionDto / TileDto / PublishedLayoutDto
// field on the mapping as it stands. The chain has three revisions (Archived, Published, Draft)
// created by different operators at different moments, so no field can be satisfied by a
// sibling's value.
public class LayoutDtoMappingCharacterisationTests
{
    private static readonly FabIdentifier Hamburg = FabIdentifier.From("hamburg");

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);
    private static readonly DateTimeOffset T3 = T0.AddHours(3);
    private static readonly DateTimeOffset T4 = T0.AddHours(4);

    private static readonly OperatorIdentifier Author = OperatorIdentifier.From(Guid.Parse("a1a1a1a1-0000-7000-8000-0000000000a1"));
    private static readonly OperatorIdentifier Publisher = OperatorIdentifier.From(Guid.Parse("a2a2a2a2-0000-7000-8000-0000000000a2"));
    private static readonly OperatorIdentifier Brancher = OperatorIdentifier.From(Guid.Parse("a3a3a3a3-0000-7000-8000-0000000000a3"));

    private static readonly CameraIdentifier CameraA = CameraIdentifier.From(Guid.Parse("c1c1c1c1-0000-7000-8000-0000000000c1"));
    private static readonly CameraIdentifier CameraB = CameraIdentifier.From(Guid.Parse("c2c2c2c2-0000-7000-8000-0000000000c2"));
    private static readonly CameraIdentifier CameraC = CameraIdentifier.From(Guid.Parse("c3c3c3c3-0000-7000-8000-0000000000c3"));
    private static readonly OverlayIdentifier OverlayB = OverlayIdentifier.From(Guid.Parse("b2b2b2b2-0000-7000-8000-0000000000b2"));

    private static readonly TileDto[] ExpectedTiles =
    [
        new TileDto(CameraA.Value, null, 0, 0, 1, 1),
        new TileDto(CameraB.Value, OverlayB.Value, 0, 1, 2, 1),
        new TileDto(CameraC.Value, null, 1, 0, 1, 1),
    ];

    private static (LayoutAggregate Layout, InMemoryLayoutRepository Repository) Chain()
    {
        // Deliberately declared out of row-then-col order.
        IReadOnlyList<Tile> tiles =
        [
            new Tile(CameraC, Option<OverlayIdentifier>.None, GridPosition.From(1, 0)),
            new Tile(CameraB, Option<OverlayIdentifier>.Some(OverlayB), GridPosition.From(0, 1), TileSpan.From(2, 1)),
            new Tile(CameraA, Option<OverlayIdentifier>.None, GridPosition.From(0, 0)),
        ];
        var layout = new LayoutBuilder()
            .WithFab(Hamburg)
            .Named("Hall-7")
            .WithGrid(GridDimensions.From(2, 2))
            .WithTiles(tiles)
            .CreatedBy(Author)
            .At(T0)
            .Build();
        layout.Publish(LayoutRevisionNumber.One, Publisher, new LayoutBuilder.TestClock(T1));
        var second = layout.BranchDraft(Brancher, new LayoutBuilder.TestClock(T2));
        layout.Publish(second.Number, Publisher, new LayoutBuilder.TestClock(T3));
        layout.BranchDraft(Brancher, new LayoutBuilder.TestClock(T4));

        InMemoryLayoutRepository repository = new();
        repository.Add(layout);
        return (layout, repository);
    }

    [Fact]
    public async Task Get_maps_the_layout_fields_and_orders_revisions_by_number()
    {
        var (layout, repository) = Chain();
        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));

        var result = await handler.HandleAsync(new GetLayoutQuery([Hamburg], layout.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.LayoutIdentifier.ShouldBe(layout.Id.Value);
        dto.Version.ShouldBe(layout.Version);
        dto.Fab.ShouldBe("hamburg");
        dto.Name.ShouldBe("Hall-7");
        dto.CreatedAt.ShouldBe(T0);
        dto.CreatedBy.ShouldBe(Author.Value);
        dto.Revisions.Select(revision => revision.RevisionNumber).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Get_maps_every_revision_field_for_an_archived_a_published_and_a_draft_revision()
    {
        var (layout, repository) = Chain();
        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));

        var result = await handler.HandleAsync(new GetLayoutQuery([Hamburg], layout.Id), CancellationToken.None);

        var revisions = result.Value.Revisions;
        var archived = revisions[0];
        var published = revisions[1];
        var draft = revisions[2];

        archived.RevisionIdentifier.ShouldBe(layout.Revisions.Single(revision => revision.Number.Value == 1).Id.Value);
        archived.State.ShouldBe(LayoutRevisionState.Archived.Value);
        archived.GridRows.ShouldBe(2);
        archived.GridCols.ShouldBe(2);
        archived.CreatedAt.ShouldBe(T0);
        archived.CreatedBy.ShouldBe(Author.Value);
        archived.PublishedAt.ShouldBe(T1);
        archived.ArchivedAt.ShouldBe(T3);

        published.RevisionIdentifier.ShouldBe(layout.Revisions.Single(revision => revision.Number.Value == 2).Id.Value);
        published.State.ShouldBe(LayoutRevisionState.Published.Value);
        published.CreatedAt.ShouldBe(T2);
        published.CreatedBy.ShouldBe(Brancher.Value);
        published.PublishedAt.ShouldBe(T3);
        published.ArchivedAt.ShouldBeNull();

        draft.RevisionIdentifier.ShouldBe(layout.Revisions.Single(revision => revision.Number.Value == 3).Id.Value);
        draft.State.ShouldBe(LayoutRevisionState.Draft.Value);
        draft.CreatedAt.ShouldBe(T4);
        draft.CreatedBy.ShouldBe(Brancher.Value);
        draft.PublishedAt.ShouldBeNull();
        draft.ArchivedAt.ShouldBeNull();

        new[] { archived.RevisionIdentifier, published.RevisionIdentifier, draft.RevisionIdentifier }
            .Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Get_orders_tiles_by_row_then_column_and_maps_overlay_and_span()
    {
        var (layout, repository) = Chain();
        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));

        var result = await handler.HandleAsync(new GetLayoutQuery([Hamburg], layout.Id), CancellationToken.None);

        result.Value.Revisions[1].Tiles.ShouldBe(ExpectedTiles);
    }

    [Fact]
    public async Task List_published_maps_the_published_revision_with_its_tiles_and_spans()
    {
        var (layout, repository) = Chain();
        ListLayoutsQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));

        var result = await handler.HandleAsync(
            new ListLayoutsQuery([Hamburg], LayoutRevisionState.Published), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.ShouldBeEmpty();
        var dto = result.Value.Published.ShouldHaveSingleItem();
        dto.LayoutIdentifier.ShouldBe(layout.Id.Value);
        dto.Name.ShouldBe("Hall-7");
        dto.RevisionNumber.ShouldBe(2);
        dto.GridRows.ShouldBe(2);
        dto.GridCols.ShouldBe(2);
        dto.PublishedAt.ShouldBe(T3);
        dto.Tiles.ShouldBe(ExpectedTiles);
    }

    [Fact]
    public async Task List_default_maps_the_full_chain_through_the_same_layout_shape_as_get()
    {
        var (layout, repository) = Chain();
        InMemoryLayoutQuerySource source = new(repository);

        var listed = await new ListLayoutsQueryHandler(source)
            .HandleAsync(new ListLayoutsQuery([Hamburg], null), CancellationToken.None);
        var fetched = await new GetLayoutQueryHandler(source)
            .HandleAsync(new GetLayoutQuery([Hamburg], layout.Id), CancellationToken.None);

        listed.Value.Published.ShouldBeEmpty();
        var dto = listed.Value.Chains.ShouldHaveSingleItem();
        dto.Revisions.Count.ShouldBe(fetched.Value.Revisions.Count);
        for (var index = 0; index < dto.Revisions.Count; index++)
        {
            dto.Revisions[index].ShouldBe(fetched.Value.Revisions[index] with { Tiles = dto.Revisions[index].Tiles });
            dto.Revisions[index].Tiles.ShouldBe(fetched.Value.Revisions[index].Tiles);
        }

        dto.CreatedAt.ShouldBe(T0);
        dto.CreatedBy.ShouldBe(Author.Value);
    }
}
