using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Application.Queries;
using SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Queries;

public class ListOverlaysQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static (InMemoryOverlayRepository repo, IOverlayQuerySource source) BuildSource()
    {
        InMemoryOverlayRepository repo = new();
        return (repo, new InMemoryOverlayQuerySource(repo));
    }

    private static Overlay Seed(InMemoryOverlayRepository overlays, string name, FakeClock clock)
    {
        Overlay overlay = Overlay.CreateDraft(
            OverlayName.From(name),
            Label.From("Hello", 0.1m, 0.1m, 0.3m, 0.08m, 32),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        overlays.Add(overlay);
        return overlay;
    }

    [Fact]
    public async Task With_no_filter_returns_every_chain_with_full_revision_history()
    {
        (InMemoryOverlayRepository repo, IOverlayQuerySource source) = BuildSource();
        FakeClock clock = new(FixedMoment);
        Seed(repo, "Line-1", clock);
        Seed(repo, "Line-2", clock);

        ListOverlaysQueryHandler handler = new(source);
        Result<ListOverlaysResult, ListOverlaysError> result = await handler.HandleAsync(
            new ListOverlaysQuery(null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.Count.ShouldBe(2);
        result.Value.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_state_Published_returns_only_chains_with_a_published_revision()
    {
        (InMemoryOverlayRepository repo, IOverlayQuerySource source) = BuildSource();
        FakeClock clock = new(FixedMoment);
        Overlay draftOnly = Seed(repo, "Line-Draft", clock);
        Overlay published = Seed(repo, "Line-Pub", clock);
        published.Publish(OverlayRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);

        ListOverlaysQueryHandler handler = new(source);
        Result<ListOverlaysResult, ListOverlaysError> result = await handler.HandleAsync(
            new ListOverlaysQuery(OverlayRevisionState.Published), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Chains.ShouldBeEmpty();
        result.Value.Published.Count.ShouldBe(1);
        result.Value.Published[0].Name.ShouldBe("Line-Pub");
        _ = draftOnly;
    }
}
