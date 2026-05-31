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

    [Fact]
    public async Task No_filter_returns_every_chain_in_the_chains_envelope()
    {
        InMemoryLayoutRepository repository = new();
        repository.Add(NewChain("Line-1"));
        repository.Add(NewChain("Line-2"));

        ListLayoutsQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<ListLayoutsResult, ListLayoutsError> result = await handler.HandleAsync(
            new ListLayoutsQuery(State: null), CancellationToken.None);

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
            new ListLayoutsQuery(State: LayoutRevisionState.Published), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.Count.ShouldBe(0);
        result.Value.Published.Count.ShouldBe(1);
        result.Value.Published.Single().Name.ShouldBe("Pub");
    }

    private static Layout NewChain(string name) =>
        new LayoutBuilder().Named(name).At(FixedMoment).Build();
}
