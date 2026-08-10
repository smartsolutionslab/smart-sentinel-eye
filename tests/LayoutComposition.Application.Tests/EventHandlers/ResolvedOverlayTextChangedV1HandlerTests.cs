using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class ResolvedOverlayTextChangedV1HandlerTests
{
    private static readonly EventMetadata TestMetadata = MetadataFor("munich");

    /// <summary>
    /// Spec 014 FR-015 made the fab load-bearing: a frame without one reaches
    /// no screen, so the relay cases carry one.
    /// </summary>
    private static EventMetadata MetadataFor(string fab) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        fab,
        null);

    [Fact]
    public async Task Relays_the_resolved_overlay_text_onto_the_broadcaster()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV1Handler handler = new(
            broadcaster, NullLogger<ResolvedOverlayTextChangedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        ResolvedOverlayTextChangedV1 message = new(
            Overlay: overlay,
            ResolvedText: "OEE: 82.5%",
            Version: 7,
            Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        ResolvedOverlayTextChangedNotification notification = broadcaster.ResolvedTextChanged.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        notification.ResolvedText.ShouldBe("OEE: 82.5%");
        notification.Version.ShouldBe(7);
        // The fab travels with it, or the broadcaster has nothing to target.
        notification.Fab.ShouldBe("munich");
    }

    [Fact]
    public async Task Carries_the_fab_the_change_happened_in()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV1Handler handler = new(
            broadcaster, NullLogger<ResolvedOverlayTextChangedV1Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV1(
                Overlay: Guid.CreateVersion7(),
                ResolvedText: "OEE: 7%",
                Version: 1,
                Metadata: MetadataFor("dresden")),
            CancellationToken.None);

        // Asserted against dresden rather than munich: everything else in the
        // suite defaults to munich, so a relay that ignored the message's fab
        // and hard-coded the default would pass the case above.
        broadcaster.ResolvedTextChanged.ShouldHaveSingleItem().Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task A_frame_with_no_fab_is_not_broadcast()
    {
        // FR-015: it cannot be addressed to anyone. Broadcasting it widely
        // would put one plant's figure on another's wall, which is the defect
        // spec 014 exists to remove rather than one to reintroduce at the
        // last hop.
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV1Handler handler = new(
            broadcaster, NullLogger<ResolvedOverlayTextChangedV1Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV1(
                Overlay: Guid.CreateVersion7(),
                ResolvedText: "OEE: 82.5%",
                Version: 7,
                Metadata: MetadataFor(null!)),
            CancellationToken.None);

        broadcaster.ResolvedTextChanged.ShouldBeEmpty();
    }
}
