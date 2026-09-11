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
    private static EventMetadata MetadataFor(string fab) => MetadataFor(fab, rootIngestedAt: null);

    private static EventMetadata MetadataFor(string fab, DateTimeOffset? rootIngestedAt) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        fab,
        null,
        rootIngestedAt);

    /// <summary>
    /// When the plant-floor event at the root of this push was accepted by
    /// EventIngestion — the near end of §IV's <c>event → overlay state</c> leg.
    /// </summary>
    private static readonly DateTimeOffset Accepted =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>
    /// How long the push itself takes in the span cases. Sized after #2072's
    /// measurement of the remainder this instrument used to miss — 555 ms and
    /// 758 ms server-side, from the value write to a frame on a subscribed
    /// client — so a test that passed while measuring the write would be
    /// measuring something this obviously different.
    /// </summary>
    private static readonly TimeSpan PushDuration = TimeSpan.FromMilliseconds(750);

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

    /// <summary>
    /// Spec 133 SC-001, the load-bearing case. §IV's <c>event → overlay state</c>
    /// leg ends when the effect is applied — for a resolved label that is the
    /// frame being pushed, not the value being written a context and a broker
    /// hop earlier.
    ///
    /// <para>
    /// <b>This asserts the span, not the call site.</b> The push spends
    /// <see cref="PushDuration"/> before it returns, and the recorded interval
    /// has to contain it. A recording taken before the broadcast reports zero
    /// and fails here — which is the counterfactual spec 133 T016 runs rather
    /// than asserts in prose. A test that merely checked the budget was called
    /// would pass either way, and that is the defect #2173 describes: an
    /// instrument carrying the leg's name while measuring a prefix of it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_recorded_span_includes_the_time_spent_pushing()
    {
        FakeClock clock = new(Accepted);
        RecordingLatencyBudget latency = new(clock);
        FakeLayoutLifecycleBroadcaster broadcaster = new()
        {
            DuringPush = () => clock.Advance(PushDuration),
        };

        ResolvedOverlayTextChangedV1Handler handler = new(
            broadcaster, NullLogger<ResolvedOverlayTextChangedV1Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV1(
                Overlay: Guid.CreateVersion7(),
                ResolvedText: "OEE: 82.5%",
                Version: 3,
                Metadata: MetadataFor("munich", Accepted)),
            CancellationToken.None);

        latency.Recorded.ShouldHaveSingleItem().ShouldBe(Accepted);
        DateTimeOffset observed = latency.ObservedAt.ShouldHaveSingleItem();
        (observed - Accepted).ShouldBeGreaterThanOrEqualTo(
            PushDuration,
            "the leg ends when the frame has been pushed, so the time the push "
            + "itself took belongs inside the measurement");
    }

    /// <summary>
    /// Spec 025 FR-005, from the handler that now owns the measurement. The leg
    /// is only measurable when the effect has a plant-floor root; a value an
    /// operator set by hand has none.
    ///
    /// <para>
    /// The failure this prevents is not a crash. It is a <c>0 ms</c> in the
    /// distribution — a perfect score for a journey nobody timed, which would
    /// drag a p99 down and make a breached budget look met.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_effect_with_no_plant_floor_root_is_not_timed()
    {
        RecordingLatencyBudget latency = new();
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV1Handler handler = new(
            broadcaster, NullLogger<ResolvedOverlayTextChangedV1Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV1(
                Overlay: Guid.CreateVersion7(),
                ResolvedText: "OEE: 82.5%",
                Version: 1,
                Metadata: MetadataFor("munich")),
            CancellationToken.None);

        broadcaster.ResolvedTextChanged.ShouldHaveSingleItem();
        latency.Recorded.ShouldHaveSingleItem().ShouldBeNull(
            "the handler must hand the absent moment to the budget and let it decide, "
            + "rather than substituting a zero that would read as an instant journey");
    }

    /// <summary>
    /// Spec 133 FR-005. A frame with no fab reaches no wall, so no overlay
    /// state was reached and there is no completed journey to time. Recording
    /// one would put the duration of a drop into a distribution describing
    /// arrivals.
    /// </summary>
    [Fact]
    public async Task A_frame_that_is_not_broadcast_is_not_timed()
    {
        RecordingLatencyBudget latency = new();
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV1Handler handler = new(
            broadcaster, NullLogger<ResolvedOverlayTextChangedV1Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV1(
                Overlay: Guid.CreateVersion7(),
                ResolvedText: "OEE: 82.5%",
                Version: 1,
                Metadata: MetadataFor(null!, Accepted)),
            CancellationToken.None);

        broadcaster.ResolvedTextChanged.ShouldBeEmpty();
        latency.Recorded.ShouldBeEmpty();
    }
}
