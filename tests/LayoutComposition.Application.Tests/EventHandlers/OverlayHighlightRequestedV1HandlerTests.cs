using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class OverlayHighlightRequestedV1HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = MetadataFor("munich");

    /// <summary>
    /// A highlight without a fab reaches no screen (spec 014 FR-015), so the
    /// relay cases carry one.
    /// </summary>
    private static EventMetadata MetadataFor(string fab) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        fab,
        null);

    [Fact]
    public async Task Calls_broadcaster_OverlayHighlightedAsync_with_the_overlay_and_duration()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayHighlightRequestedV1Handler handler = new(
            broadcaster, new RecordingLatencyBudget(), NullLogger<OverlayHighlightRequestedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayHighlightRequestedV1 message = new(overlay, 10_000, Moment, Guid.CreateVersion7(), Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        OverlayHighlightedNotification notification = broadcaster.Highlighted.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        notification.DurationMs.ShouldBe(10_000);
        notification.Fab.ShouldBe("munich");
    }

    [Fact]
    public async Task Two_highlights_of_one_event_on_one_overlay_are_both_broadcast()
    {
        // These two frames share their overlay AND their CausingEventIdentifier
        // by design, so a consumer keyed on that pair would collapse them; the
        // correct redelivery key is Metadata.EventIdentifier (#2214).
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayHighlightRequestedV1Handler handler = new(
            broadcaster, new RecordingLatencyBudget(), NullLogger<OverlayHighlightRequestedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        Guid causingEvent = Guid.CreateVersion7();

        await handler.Handle(
            new OverlayHighlightRequestedV1(
                overlay, 5_000, Moment, causingEvent,
                new EventMetadata(Guid.CreateVersion7(), Moment, "munich", null)),
            CancellationToken.None);
        await handler.Handle(
            new OverlayHighlightRequestedV1(
                overlay, 12_000, Moment, causingEvent,
                new EventMetadata(Guid.CreateVersion7(), Moment, "munich", null)),
            CancellationToken.None);

        broadcaster.Highlighted.Count.ShouldBe(2);
        broadcaster.Highlighted[0].Overlay.ShouldBe(overlay);
        broadcaster.Highlighted[0].DurationMs.ShouldBe(5_000);
        broadcaster.Highlighted[1].Overlay.ShouldBe(overlay);
        broadcaster.Highlighted[1].DurationMs.ShouldBe(12_000);
    }

    [Fact]
    public async Task Carries_the_fab_of_the_rule_that_requested_it()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayHighlightRequestedV1Handler handler = new(
            broadcaster, new RecordingLatencyBudget(), NullLogger<OverlayHighlightRequestedV1Handler>.Instance);

        await handler.Handle(
            new OverlayHighlightRequestedV1(
                Guid.CreateVersion7(), 5_000, Moment, Guid.CreateVersion7(), MetadataFor("dresden")),
            CancellationToken.None);

        // dresden, not munich: everything else defaults to munich, so a relay
        // that ignored the message's fab and hard-coded the default would pass
        // the case above.
        broadcaster.Highlighted.ShouldHaveSingleItem().Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task A_highlight_with_no_fab_is_not_broadcast()
    {
        // It cannot be addressed to anyone, and a highlight lights up a wall —
        // sending one plant's to every plant is a visible cross-fab effect,
        // not merely leaked metadata (#1397).
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayHighlightRequestedV1Handler handler = new(
            broadcaster, new RecordingLatencyBudget(), NullLogger<OverlayHighlightRequestedV1Handler>.Instance);

        await handler.Handle(
            new OverlayHighlightRequestedV1(
                Guid.CreateVersion7(), 10_000, Moment, Guid.CreateVersion7(), MetadataFor(null!)),
            CancellationToken.None);

        broadcaster.Highlighted.ShouldBeEmpty();
    }
}
