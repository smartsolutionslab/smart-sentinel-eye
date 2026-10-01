using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

/// <summary>
/// Spec 296 T113, plan.md §3.1. <c>WallSceneSwitchRequestedV1Handler</c> —
/// the rule-driven counterpart of <c>SwitchWallSceneCommandHandler</c>, with
/// no expected version (PD-1, FR-006), a dedup reservation (FR-012), and
/// FR-010's six named drops in place of the manual path's 400/409. Fakes
/// mirror <c>SwitchWallSceneCommandHandlerTests</c> plus a dedup store and a
/// capturing logger, following
/// <c>SystemVariableValueRequestedV1HandlerTests</c>' own shape for the
/// same kind of rule-effect handler.
/// </summary>
public class WallSceneSwitchRequestedV1HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-09-30T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private sealed class FakeDedupStore : IWallSwitchRequestDedupStore
    {
        public HashSet<(Guid Rule, Guid CausingEvent)> Reserved { get; } = [];
        public List<(Guid Rule, Guid CausingEvent, Guid Wall)> Calls { get; } = [];

        public Task<bool> TryReserveAsync(
            RuleIdentifier rule, CausingEventIdentifier causingEvent, WallIdentifier wall,
            CancellationToken cancellationToken)
        {
            Calls.Add((rule.Value, causingEvent.Value, wall.Value));
            return Task.FromResult(Reserved.Add((rule.Value, causingEvent.Value)));
        }
    }

    private static WallSceneSwitchRequestedV1Handler Handler(
        InMemoryWallRepository walls,
        FakeLayoutPublicationLookup lookup,
        FakeDedupStore dedup,
        ILogger<WallSceneSwitchRequestedV1Handler>? logger = null) =>
        new(walls, lookup, dedup, new FakeClock(Moment),
            logger ?? NullLogger<WallSceneSwitchRequestedV1Handler>.Instance);

    private static WallSceneSwitchRequestedV1 Request(
        Wall wall, string target, LayoutIdentifier? targetLayout, Guid rule, Guid causingEvent, string? fab = "munich") =>
        new(
            wall.Id.Value, target, targetLayout?.Value, rule, Moment, causingEvent,
            Metadata: new EventMetadata(Guid.CreateVersion7(), Moment, fab, null, Moment));

    private static FakeLayoutPublicationLookup AllPublishedIn(FabIdentifier fab, params LayoutIdentifier[] scenes)
    {
        FakeLayoutPublicationLookup lookup = new();
        foreach (LayoutIdentifier scene in scenes)
        {
            lookup.WithLayout(scene, fab);
        }
        return lookup;
    }

    // ---- FR-006: applies the request against whatever is current, no expected version ----

    [Fact]
    public async Task A_Layout_target_switches_the_wall_with_cause_Rule()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), new FakeDedupStore());

        Guid rule = Guid.CreateVersion7();
        Guid causingEvent = Guid.CreateVersion7();
        await handler.Handle(Request(wall, "Layout", b, rule, causingEvent), CancellationToken.None);

        wall.Showing.ShouldBe(b);
        wall.SceneVersion.Value.ShouldBe(1);
    }

    [Fact]
    public async Task A_Next_target_switches_the_wall_with_cause_Rule()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), new FakeDedupStore());

        await handler.Handle(
            Request(wall, "Next", null, Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        wall.Showing.ShouldBe(b);
    }

    /// <summary>
    /// PD-1 / FR-006: unlike the manual command, no expected version is
    /// consulted at all — a stale caller cannot refuse this the way
    /// <c>SwitchWallSceneCommandHandler</c> refuses one with
    /// <c>WALL_STALE</c>. The request still applies even though the wall's
    /// version has moved since the rule last read it.
    /// </summary>
    [Fact]
    public async Task The_wall_switches_even_though_its_version_has_moved_since_the_rule_last_saw_it()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b, c]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = AllPublishedIn(Munich, a, b, c);
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, lookup, new FakeDedupStore());

        // Moves the version on, as an intervening manual switch would.
        await handler.Handle(
            Request(wall, "Layout", b, Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);
        int versionAfterFirstSwitch = wall.Version;

        await handler.Handle(
            Request(wall, "Layout", c, Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        wall.Showing.ShouldBe(c);
        ((int)wall.Version).ShouldBeGreaterThan(versionAfterFirstSwitch);
    }

    // ---- FR-010: six named drops, nothing published, nothing consumed from the dedup key (a)-(c)/(f) ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotAFab")]
    public async Task FR010a_An_absent_or_unparseable_fab_is_dropped_and_logged(string? fab)
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), dedup, logger);

        Guid rule = Guid.CreateVersion7();
        Guid causingEvent = Guid.CreateVersion7();
        await handler.Handle(Request(wall, "Layout", b, rule, causingEvent, fab), CancellationToken.None);

        wall.Showing.ShouldBe(a);
        wall.PendingEvents.ShouldBeEmpty();
        dedup.Calls.ShouldBeEmpty();

        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(wall.Id.ToString());
        entry.Message.ShouldContain(rule.ToString());
        entry.Message.ShouldContain(causingEvent.ToString());
    }

    [Fact]
    public async Task FR010b_A_wall_existing_in_another_fab_is_dropped_and_both_fabs_are_named()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), dedup, logger);

        await handler.Handle(
            Request(wall, "Layout", b, Guid.CreateVersion7(), Guid.CreateVersion7(), fab: Dresden.Value),
            CancellationToken.None);

        wall.Showing.ShouldBe(a);
        wall.PendingEvents.ShouldBeEmpty();
        dedup.Calls.ShouldBeEmpty();

        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(wall.Id.ToString());
        entry.Message.ShouldContain("dresden");
        entry.Message.ShouldContain("munich");
    }

    [Fact]
    public async Task FR010c_An_unknown_wall_is_dropped_and_logged()
    {
        InMemoryWallRepository walls = new(); // empty — the wall identifier exists nowhere
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        FakeLayoutPublicationLookup lookup = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, lookup, dedup, logger);

        Guid unknownWall = Guid.CreateVersion7();
        WallSceneSwitchRequestedV1 message = new(
            unknownWall, "Next", null, Guid.CreateVersion7(), Moment, Guid.CreateVersion7(),
            Metadata: new EventMetadata(Guid.CreateVersion7(), Moment, Munich.Value, null, Moment));

        await handler.Handle(message, CancellationToken.None);

        walls.Walls.ShouldBeEmpty();
        dedup.Calls.ShouldBeEmpty();

        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(unknownWall.ToString());
    }

    /// <summary>US2-8/9: the cross-fab drop (b) and the unknown-wall drop (c) must not read alike.</summary>
    [Fact]
    public async Task FR010b_and_FR010c_log_distinct_messages()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();

        InMemoryWallRepository crossFabWalls = new();
        crossFabWalls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> crossFabLogger = new();
        await Handler(crossFabWalls, AllPublishedIn(Munich, a, b), new FakeDedupStore(), crossFabLogger).Handle(
            Request(wall, "Layout", b, Guid.CreateVersion7(), Guid.CreateVersion7(), fab: Dresden.Value),
            CancellationToken.None);

        InMemoryWallRepository emptyWalls = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> unknownWallLogger = new();
        Guid unknownWall = Guid.CreateVersion7();
        await Handler(emptyWalls, new FakeLayoutPublicationLookup(), new FakeDedupStore(), unknownWallLogger).Handle(
            new WallSceneSwitchRequestedV1(
                unknownWall, "Layout", b.Value, Guid.CreateVersion7(), Moment, Guid.CreateVersion7(),
                Metadata: new EventMetadata(Guid.CreateVersion7(), Moment, Munich.Value, null, Moment)),
            CancellationToken.None);

        crossFabLogger.Entries.ShouldHaveSingleItem().Message
            .ShouldNotBe(unknownWallLogger.Entries.ShouldHaveSingleItem().Message);
    }

    [Fact]
    public async Task FR010d_A_target_not_in_the_wall_scene_set_is_dropped_and_the_layout_is_named()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier outside = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), dedup, logger);

        await handler.Handle(
            Request(wall, "Layout", outside, Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        wall.Showing.ShouldBe(a);
        wall.PendingEvents.ShouldBeEmpty();

        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(outside.ToString());
    }

    [Fact]
    public async Task FR010e_A_target_no_longer_Published_is_dropped_and_the_layout_is_named()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich).WithLayout(b, Munich, isPublished: false);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, lookup, dedup, logger);

        await handler.Handle(
            Request(wall, "Layout", b, Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        wall.Showing.ShouldBe(a);
        wall.PendingEvents.ShouldBeEmpty();

        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(b.ToString());
    }

    [Fact]
    public async Task FR010f_An_unknown_target_contract_violation_is_dropped_and_logged()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), dedup, logger);

        WallSceneSwitchRequestedV1 message = new(
            wall.Id.Value, "Sideways", null, Guid.CreateVersion7(), Moment, Guid.CreateVersion7(),
            Metadata: new EventMetadata(Guid.CreateVersion7(), Moment, Munich.Value, null, Moment));
        await handler.Handle(message, CancellationToken.None);

        wall.Showing.ShouldBe(a);
        wall.PendingEvents.ShouldBeEmpty();
        dedup.Calls.ShouldBeEmpty();
        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task FR010f_Layout_with_no_TargetLayout_is_dropped_and_logged()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), dedup, logger);

        WallSceneSwitchRequestedV1 message = new(
            wall.Id.Value, "Layout", null, Guid.CreateVersion7(), Moment, Guid.CreateVersion7(),
            Metadata: new EventMetadata(Guid.CreateVersion7(), Moment, Munich.Value, null, Moment));
        await handler.Handle(message, CancellationToken.None);

        wall.Showing.ShouldBe(a);
        wall.PendingEvents.ShouldBeEmpty();
        dedup.Calls.ShouldBeEmpty();
        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    // ---- No-ops: distinct from the warnings above, logged at information level ----

    [Fact]
    public async Task Already_showing_the_target_is_a_no_op_logged_at_information_level()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), dedup, logger);

        await handler.Handle(
            Request(wall, "Layout", a, Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        wall.SceneVersion.Value.ShouldBe(0);
        wall.PendingEvents.ShouldBeEmpty();

        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task Next_finding_nothing_else_Published_is_a_no_op_logged_at_information_level()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        // Only `a` (Showing) is Published; Next has nowhere else to go (PD-6).
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup().WithLayout(a, Munich);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, lookup, dedup, logger);

        await handler.Handle(
            Request(wall, "Next", null, Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        wall.Showing.ShouldBe(a);
        wall.PendingEvents.ShouldBeEmpty();

        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
    }

    // ---- FR-012: applied at most once per (rule, causingEvent) ----

    [Fact]
    public async Task A_second_delivery_with_the_same_rule_and_causing_event_is_a_dedup_hit()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b, c]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = AllPublishedIn(Munich, a, b, c);
        FakeDedupStore dedup = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, lookup, dedup, logger);

        Guid rule = Guid.CreateVersion7();
        Guid causingEvent = Guid.CreateVersion7();
        await handler.Handle(Request(wall, "Layout", b, rule, causingEvent), CancellationToken.None);
        int versionAfterFirst = wall.Version;

        // A redelivery or an upstream re-run — same (rule, causingEvent), a
        // different envelope, and (deliberately) a different target, so a
        // handler that ignored the dedup hit would be caught moving the wall.
        await handler.Handle(Request(wall, "Layout", c, rule, causingEvent), CancellationToken.None);

        wall.Showing.ShouldBe(b);
        ((int)wall.Version).ShouldBe(versionAfterFirst);

        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information || entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Two_different_rules_firing_on_the_same_event_both_apply()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b, c]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeLayoutPublicationLookup lookup = AllPublishedIn(Munich, a, b, c);
        FakeDedupStore dedup = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, lookup, dedup);

        Guid causingEvent = Guid.CreateVersion7();
        await handler.Handle(
            Request(wall, "Layout", b, Guid.CreateVersion7(), causingEvent), CancellationToken.None);
        await handler.Handle(
            Request(wall, "Layout", c, Guid.CreateVersion7(), causingEvent), CancellationToken.None);

        wall.Showing.ShouldBe(c);
        wall.SceneVersion.Value.ShouldBe(2);
        dedup.Reserved.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Drops_a_through_c_never_consume_a_dedup_key()
    {
        // (a) absent fab
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        FakeDedupStore dedup = new();
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), dedup);
        await handler.Handle(
            Request(wall, "Layout", b, Guid.CreateVersion7(), Guid.CreateVersion7(), fab: null),
            CancellationToken.None);

        // (b) another fab
        await handler.Handle(
            Request(wall, "Layout", b, Guid.CreateVersion7(), Guid.CreateVersion7(), fab: Dresden.Value),
            CancellationToken.None);

        // (c) unknown wall
        await handler.Handle(
            new WallSceneSwitchRequestedV1(
                Guid.CreateVersion7(), "Next", null, Guid.CreateVersion7(), Moment, Guid.CreateVersion7(),
                Metadata: new EventMetadata(Guid.CreateVersion7(), Moment, Munich.Value, null, Moment)),
            CancellationToken.None);

        dedup.Calls.ShouldBeEmpty();
    }

    // ---- FR-011: a lost optimistic-concurrency race is never retried or swallowed ----

    [Fact]
    public async Task A_DbUpdateConcurrencyException_from_SaveAsync_propagates_out_of_Handle()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(Moment).Build();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        walls.FailNextSaveWith(new DbUpdateConcurrencyException());
        WallSceneSwitchRequestedV1Handler handler = Handler(walls, AllPublishedIn(Munich, a, b), new FakeDedupStore());

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => handler.Handle(
                Request(wall, "Layout", b, Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None));
    }
}
