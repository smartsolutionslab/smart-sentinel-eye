using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Application.Queries;
using SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;
using OverlayAggregate = SmartSentinelEye.OverlayDesigner.Domain.Overlay.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Queries;

// Spec 459 (#3154), phase 4a: every OverlayDto / OverlayRevisionDto / OverlayElementDto /
// PublishedOverlayDto field on the mapping as it stands. The chain has three revisions
// (Archived, Published, Draft) created by different operators at different moments, and the
// elements carry distinct position/size components, so no field can be satisfied by a sibling.
public class OverlayDtoMappingCharacterisationTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-08-01T08:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);
    private static readonly DateTimeOffset T3 = T0.AddHours(3);
    private static readonly DateTimeOffset T4 = T0.AddHours(4);

    private static readonly OperatorIdentifier Author = OperatorIdentifier.From(Guid.Parse("d1d1d1d1-0000-7000-8000-0000000000d1"));
    private static readonly OperatorIdentifier Publisher = OperatorIdentifier.From(Guid.Parse("d2d2d2d2-0000-7000-8000-0000000000d2"));
    private static readonly OperatorIdentifier Brancher = OperatorIdentifier.From(Guid.Parse("d3d3d3d3-0000-7000-8000-0000000000d3"));

    private static readonly OverlayColor TextColor = OverlayColor.From("#9F9F9FFF");
    private static readonly OverlayColor BoxColor = OverlayColor.From("#12AB34");

    private static IReadOnlyList<OverlayElement> Elements() =>
    [
        OverlayElement.TextElement("Line 4 OK", 36, NormalizedPosition.From(0.1m, 0.2m), NormalizedSize.From(0.3m, 0.05m), TextColor),
        OverlayElement.Box(NormalizedPosition.From(0.4m, 0.5m), NormalizedSize.From(0.2m, 0.25m), BoxColor),
    ];

    private static (OverlayAggregate Overlay, IOverlayQuerySource Source) Chain()
    {
        var overlay = new OverlayBuilder()
            .Named("Line-4 status")
            .WithLabels(Elements())
            .CreatedBy(Author)
            .At(T0)
            .Build();
        overlay.Publish(OverlayRevisionNumber.One, Publisher, new OverlayBuilder.TestClock(T1));
        var second = overlay.BranchDraft(Brancher, new OverlayBuilder.TestClock(T2));
        overlay.Publish(second.Number, Publisher, new OverlayBuilder.TestClock(T3));
        overlay.BranchDraft(Brancher, new OverlayBuilder.TestClock(T4));

        InMemoryOverlayRepository repository = new();
        repository.Add(overlay);
        return (overlay, new InMemoryOverlayQuerySource(repository));
    }

    private static OverlayElementDto ExpectedText() =>
        new("Text", TextColor.Value, 0.1m, 0.2m, 0.3m, 0.05m, "Line 4 OK", 36);

    private static OverlayElementDto ExpectedBox() =>
        new("Box", BoxColor.Value, 0.4m, 0.5m, 0.2m, 0.25m, null, null);

    [Fact]
    public async Task Get_maps_the_overlay_fields_and_orders_revisions_by_number()
    {
        var (overlay, source) = Chain();

        var result = await new GetOverlayQueryHandler(source)
            .HandleAsync(new GetOverlayQuery(overlay.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.OverlayIdentifier.ShouldBe(overlay.Id.Value);
        dto.Version.ShouldBe(overlay.Version);
        dto.Name.ShouldBe("Line-4 status");
        dto.CreatedAt.ShouldBe(T0);
        dto.CreatedBy.ShouldBe(Author.Value);
        dto.Revisions.Select(revision => revision.RevisionNumber).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Get_maps_every_revision_field_for_an_archived_a_published_and_a_draft_revision()
    {
        var (overlay, source) = Chain();

        var result = await new GetOverlayQueryHandler(source)
            .HandleAsync(new GetOverlayQuery(overlay.Id), CancellationToken.None);

        var archived = result.Value.Revisions[0];
        var published = result.Value.Revisions[1];
        var draft = result.Value.Revisions[2];

        archived.RevisionIdentifier.ShouldBe(overlay.Revisions.Single(revision => revision.Number.Value == 1).Id.Value);
        archived.State.ShouldBe(OverlayRevisionState.Archived.Value);
        archived.CreatedAt.ShouldBe(T0);
        archived.CreatedBy.ShouldBe(Author.Value);
        archived.PublishedAt.ShouldBe(T1);
        archived.ArchivedAt.ShouldBe(T3);

        published.RevisionIdentifier.ShouldBe(overlay.Revisions.Single(revision => revision.Number.Value == 2).Id.Value);
        published.State.ShouldBe(OverlayRevisionState.Published.Value);
        published.CreatedAt.ShouldBe(T2);
        published.CreatedBy.ShouldBe(Brancher.Value);
        published.PublishedAt.ShouldBe(T3);
        published.ArchivedAt.ShouldBeNull();

        draft.RevisionIdentifier.ShouldBe(overlay.Revisions.Single(revision => revision.Number.Value == 3).Id.Value);
        draft.State.ShouldBe(OverlayRevisionState.Draft.Value);
        draft.CreatedAt.ShouldBe(T4);
        draft.CreatedBy.ShouldBe(Brancher.Value);
        draft.PublishedAt.ShouldBeNull();
        draft.ArchivedAt.ShouldBeNull();

        new[] { archived.RevisionIdentifier, published.RevisionIdentifier, draft.RevisionIdentifier }
            .Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Get_maps_a_text_element_and_a_shape_element_field_by_field_in_declared_order()
    {
        var (overlay, source) = Chain();

        var result = await new GetOverlayQueryHandler(source)
            .HandleAsync(new GetOverlayQuery(overlay.Id), CancellationToken.None);

        var elements = result.Value.Revisions[1].Elements;
        elements.Count.ShouldBe(2);
        elements[0].ShouldBe(ExpectedText());
        elements[1].ShouldBe(ExpectedBox());
    }

    [Fact]
    public async Task List_published_maps_the_published_revision_and_orders_names_ignoring_case()
    {
        var (overlay, _) = Chain();
        var later = new OverlayBuilder().Named("Bravo").WithLabels(Elements()).CreatedBy(Author).At(T0).Build();
        later.Publish(OverlayRevisionNumber.One, Publisher, new OverlayBuilder.TestClock(T4));
        var earlier = new OverlayBuilder().Named("alpha").WithLabels(Elements()).CreatedBy(Author).At(T0).Build();
        earlier.Publish(OverlayRevisionNumber.One, Publisher, new OverlayBuilder.TestClock(T1));
        InMemoryOverlayRepository repository = new();
        repository.Add(later);
        repository.Add(earlier);
        repository.Add(overlay);

        var result = await new ListOverlaysQueryHandler(new InMemoryOverlayQuerySource(repository))
            .HandleAsync(new ListOverlaysQuery(OverlayRevisionState.Published), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.ShouldBeEmpty();
        result.Value.Published.Select(dto => dto.Name).ShouldBe(["alpha", "Bravo", "Line-4 status"]);
        var dto = result.Value.Published.Single(published => published.OverlayIdentifier == overlay.Id.Value);
        dto.RevisionNumber.ShouldBe(2);
        dto.PublishedAt.ShouldBe(T3);
        dto.Elements.Count.ShouldBe(2);
        dto.Elements[0].ShouldBe(ExpectedText());
        dto.Elements[1].ShouldBe(ExpectedBox());
        result.Value.Published.Single(published => published.Name == "Bravo").PublishedAt.ShouldBe(T4);
    }

    [Fact]
    public async Task List_default_returns_the_chains_newest_first_with_full_overlay_fields()
    {
        var (overlay, _) = Chain();
        var newer = new OverlayBuilder().Named("Newer").WithLabels(Elements()).CreatedBy(Publisher).At(T4).Build();
        InMemoryOverlayRepository repository = new();
        repository.Add(overlay);
        repository.Add(newer);

        var result = await new ListOverlaysQueryHandler(new InMemoryOverlayQuerySource(repository))
            .HandleAsync(new ListOverlaysQuery(null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Published.ShouldBeEmpty();
        result.Value.Chains.Select(dto => dto.Name).ShouldBe(["Newer", "Line-4 status"]);
        result.Value.Chains[0].CreatedAt.ShouldBe(T4);
        result.Value.Chains[0].CreatedBy.ShouldBe(Publisher.Value);
        result.Value.Chains[1].CreatedAt.ShouldBe(T0);
        result.Value.Chains[1].CreatedBy.ShouldBe(Author.Value);
        result.Value.Chains[1].Revisions.Count.ShouldBe(3);
    }
}
