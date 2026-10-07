using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

/// <summary>
/// Spec 301 (#2720) US2, T011 — the V3 relay. Mirrors
/// <c>ResolvedOverlayTextChangedV2HandlerTests</c> (left in place; V2's own
/// production type is not deleted until T008 lands), carrying pairs instead
/// of a positional list.
///
/// <para>
/// <b>Red by construction.</b> Neither <c>ResolvedOverlayTextChangedV3</c>
/// nor <c>ResolvedOverlayTextChangedV3Handler</c> exists on today's code —
/// this whole file fails to resolve until T008 (the contract cut) and T011
/// (the handler) land.
/// </para>
/// </summary>
public class ResolvedOverlayTextChangedV3HandlerTests
{
    private static readonly EventMetadata TestMetadata = MetadataFor("munich");

    private static EventMetadata MetadataFor(string fab) => MetadataFor(fab, rootIngestedAt: null);

    private static EventMetadata MetadataFor(string fab, DateTimeOffset? rootIngestedAt) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        fab,
        null,
        rootIngestedAt);

    private static readonly DateTimeOffset Accepted =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly TimeSpan PushDuration = TimeSpan.FromMilliseconds(750);

    [Fact]
    public async Task Relays_the_resolved_overlay_text_pairs_onto_the_broadcaster()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV3Handler handler = new(
            broadcaster, new RecordingLatencyBudget(), NullLogger<ResolvedOverlayTextChangedV3Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        ResolvedOverlayTextChangedV3 message = new(
            Overlay: overlay,
            Texts: [new ResolvedOverlayTextV3("OEE: {{oee}}%", "OEE: 82.5%")],
            Version: 7,
            Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        ResolvedOverlayTextChangedNotification notification = broadcaster.ResolvedTextChanged.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        ResolvedOverlayText pair = notification.Texts.ShouldHaveSingleItem();
        pair.Template.ShouldBe("OEE: {{oee}}%");
        pair.Resolved.ShouldBe("OEE: 82.5%");
        notification.Version.ShouldBe(7);
        notification.Fab.ShouldBe("munich");
    }

    /// <summary>Spec 301 FR-005: every distinct template of the set relays under one version.</summary>
    [Fact]
    public async Task Relays_every_pair_of_the_set_under_one_version()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV3Handler handler = new(
            broadcaster, new RecordingLatencyBudget(), NullLogger<ResolvedOverlayTextChangedV3Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        ResolvedOverlayTextChangedV3 message = new(
            Overlay: overlay,
            Texts: [new ResolvedOverlayTextV3("A {{x}}", "OEE: 82.5%"), new ResolvedOverlayTextV3("B {{x}}", "Running")],
            Version: 7,
            Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        ResolvedOverlayTextChangedNotification notification = broadcaster.ResolvedTextChanged.ShouldHaveSingleItem();
        notification.Texts.Count.ShouldBe(2);
        notification.Texts[0].Template.ShouldBe("A {{x}}");
        notification.Texts[0].Resolved.ShouldBe("OEE: 82.5%");
        notification.Texts[1].Template.ShouldBe("B {{x}}");
        notification.Texts[1].Resolved.ShouldBe("Running");
        notification.Version.ShouldBe(7);
    }

    [Fact]
    public async Task Carries_the_fab_the_change_happened_in()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV3Handler handler = new(
            broadcaster, new RecordingLatencyBudget(), NullLogger<ResolvedOverlayTextChangedV3Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV3(
                Overlay: Guid.CreateVersion7(),
                Texts: [new ResolvedOverlayTextV3("OEE: {{oee}}%", "OEE: 7%")],
                Version: 1,
                Metadata: MetadataFor("dresden")),
            CancellationToken.None);

        broadcaster.ResolvedTextChanged.ShouldHaveSingleItem().Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task A_frame_with_no_fab_is_not_broadcast()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV3Handler handler = new(
            broadcaster, new RecordingLatencyBudget(), NullLogger<ResolvedOverlayTextChangedV3Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV3(
                Overlay: Guid.CreateVersion7(),
                Texts: [new ResolvedOverlayTextV3("OEE: {{oee}}%", "OEE: 82.5%")],
                Version: 7,
                Metadata: MetadataFor(null!)),
            CancellationToken.None);

        broadcaster.ResolvedTextChanged.ShouldBeEmpty();
    }

    /// <summary>Spec 133 SC-001 (relocated from the V2 handler's own suite — the measurement is unchanged by the contract cut).</summary>
    [Fact]
    public async Task The_recorded_span_includes_the_time_spent_pushing()
    {
        FakeClock clock = new(Accepted);
        RecordingLatencyBudget latency = new(clock);
        FakeLayoutLifecycleBroadcaster broadcaster = new()
        {
            DuringPush = () => clock.Advance(PushDuration),
        };

        ResolvedOverlayTextChangedV3Handler handler = new(
            broadcaster, latency, NullLogger<ResolvedOverlayTextChangedV3Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV3(
                Overlay: Guid.CreateVersion7(),
                Texts: [new ResolvedOverlayTextV3("OEE: {{oee}}%", "OEE: 82.5%")],
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

    [Fact]
    public async Task An_effect_with_no_plant_floor_root_is_not_timed()
    {
        RecordingLatencyBudget latency = new();
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV3Handler handler = new(
            broadcaster, latency, NullLogger<ResolvedOverlayTextChangedV3Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV3(
                Overlay: Guid.CreateVersion7(),
                Texts: [new ResolvedOverlayTextV3("OEE: {{oee}}%", "OEE: 82.5%")],
                Version: 1,
                Metadata: MetadataFor("munich")),
            CancellationToken.None);

        broadcaster.ResolvedTextChanged.ShouldHaveSingleItem();
        latency.Recorded.ShouldHaveSingleItem().ShouldBeNull(
            "the handler must hand the absent moment to the budget and let it decide, "
            + "rather than substituting a zero that would read as an instant journey");
    }

    [Fact]
    public async Task A_frame_that_is_not_broadcast_is_not_timed()
    {
        RecordingLatencyBudget latency = new();
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV3Handler handler = new(
            broadcaster, latency, NullLogger<ResolvedOverlayTextChangedV3Handler>.Instance);

        await handler.Handle(
            new ResolvedOverlayTextChangedV3(
                Overlay: Guid.CreateVersion7(),
                Texts: [new ResolvedOverlayTextV3("OEE: {{oee}}%", "OEE: 82.5%")],
                Version: 1,
                Metadata: MetadataFor(null!, Accepted)),
            CancellationToken.None);

        broadcaster.ResolvedTextChanged.ShouldBeEmpty();
        latency.Recorded.ShouldBeEmpty();
    }
}
