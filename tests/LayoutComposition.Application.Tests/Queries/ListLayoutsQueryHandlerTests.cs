using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Queries;

public class ListLayoutsQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    [Fact]
    public async Task No_filter_returns_every_chain_in_the_chains_envelope()
    {
        InMemoryLayoutRepository repository = new();
        repository.Add(NewChain("Line-1"));
        repository.Add(NewChain("Line-2"));

        ListLayoutsQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<ListLayoutsResult, ListLayoutsError> result = await handler.HandleAsync(
            new ListLayoutsQuery([Munich], State: null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.Count.ShouldBe(2);
        result.Value.Published.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Published_filter_returns_only_chains_with_a_Published_revision()
    {
        InMemoryLayoutRepository repository = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier op = OperatorIdentifier.From(Guid.CreateVersion7());

        Layout draftOnly = NewChain("Drf");
        Layout published = NewChain("Pub");
        published.Publish(LayoutRevisionNumber.One, op, clock);
        repository.Add(draftOnly);
        repository.Add(published);

        ListLayoutsQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<ListLayoutsResult, ListLayoutsError> result = await handler.HandleAsync(
            new ListLayoutsQuery([Munich], State: LayoutRevisionState.Published), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.Count.ShouldBe(0);
        result.Value.Published.Count.ShouldBe(1);
        result.Value.Published.Single().Name.ShouldBe("Pub");
    }

    /// <summary>FR-005 on the admin listing.</summary>
    [Fact]
    public async Task The_listing_omits_a_fab_the_caller_does_not_hold()
    {
        InMemoryLayoutRepository repository = new();
        repository.Add(NewChain(Munich, "In-Munich"));
        repository.Add(NewChain(Dresden, "In-Dresden"));

        ListLayoutsQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<ListLayoutsResult, ListLayoutsError> result = await handler.HandleAsync(
            new ListLayoutsQuery([Dresden], State: null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.Select(dto => dto.Name).ShouldBe(["In-Dresden"]);
    }

    [Fact]
    public async Task A_multi_fab_caller_sees_both_plants()
    {
        InMemoryLayoutRepository repository = new();
        repository.Add(NewChain(Munich, "In-Munich"));
        repository.Add(NewChain(Dresden, "In-Dresden"));

        ListLayoutsQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<ListLayoutsResult, ListLayoutsError> result = await handler.HandleAsync(
            new ListLayoutsQuery([Munich, Dresden], State: null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.Select(dto => dto.Fab).ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    /// <summary>
    /// The kiosk picker is scoped too. It leaks as readily as the admin list
    /// and is the shape a screen actually consumes, so a filter applied to
    /// only one of the two branches would be the easier mistake.
    /// </summary>
    [Fact]
    public async Task The_published_picker_omits_a_fab_the_caller_does_not_hold()
    {
        InMemoryLayoutRepository repository = new();
        Layout inMunich = NewChain(Munich, "Pub-Munich");
        LayoutBuilder builder = new LayoutBuilder().WithFab(Dresden).Named("Pub-Dresden").At(FixedMoment);
        Layout inDresden = builder.Build();
        inMunich.Publish(LayoutRevisionNumber.One, builder.Operator, builder.Clock);
        inDresden.Publish(LayoutRevisionNumber.One, builder.Operator, builder.Clock);
        repository.Add(inMunich);
        repository.Add(inDresden);

        ListLayoutsQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<ListLayoutsResult, ListLayoutsError> result = await handler.HandleAsync(
            new ListLayoutsQuery([Dresden], State: LayoutRevisionState.Published), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Published.Select(dto => dto.Name).ShouldBe(["Pub-Dresden"]);
    }

    private static Layout NewChain(string name) =>
        new LayoutBuilder().Named(name).At(FixedMoment).Build();

    private static Layout NewChain(FabIdentifier fab, string name) =>
        new LayoutBuilder().WithFab(fab).Named(name).At(FixedMoment).Build();
}
