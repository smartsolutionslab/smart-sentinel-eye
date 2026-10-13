using System.Globalization;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

/// <summary>
/// Characterisation (spec 345, ADR-0144): pins the exact rendered text of every
/// log line the rule-driven switch handler writes, with three <b>distinct</b>
/// identifiers for wall, rule and causing event. <see cref="WallSceneSwitchRequestedV1HandlerTests"/>
/// asserts with <c>ShouldContain</c>, which a transposed rule and event
/// satisfies; this is the only observable trace of a drop on this path, so the
/// slots must not drift when the helpers are re-signed.
/// </summary>
public class WallSceneSwitchRequestLogSlotCharacterisationTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-09-30T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private sealed class FakeDedupStore : IWallSwitchRequestDedupStore
    {
        public HashSet<(Guid Rule, Guid CausingEvent)> Reserved { get; } = [];

        public Task<bool> TryReserveAsync(
            RuleIdentifier rule,
            CausingEventIdentifier causingEvent,
            WallIdentifier wall,
            CancellationToken cancellationToken) =>
            Task.FromResult(Reserved.Add((rule.Value, causingEvent.Value)));
    }

    private static WallSceneSwitchRequestedV1Handler Handler(
        InMemoryWallRepository walls,
        FakeLayoutPublicationLookup lookup,
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger,
        FakeDedupStore? dedup = null) =>
        new(walls, lookup, dedup ?? new FakeDedupStore(), new FakeClock(Moment), logger);

    private static WallSceneSwitchRequestedV1 Request(
        Guid wall,
        string target,
        LayoutIdentifier? targetLayout,
        Guid rule,
        Guid causingEvent,
        string? fab = "munich") =>
        new(
            wall,
            target,
            targetLayout?.Value,
            rule,
            Moment,
            causingEvent,
            Metadata: new EventMetadata(Guid.CreateVersion7(), Moment, fab, null, Moment));

    private static Wall NewWall(LayoutIdentifier showing, LayoutIdentifier other)
    {
        var wall = new WallBuilder().WithFab(Munich).WithScenes([showing, other]).At(Moment).Build();
        wall.ClearPendingEvents();
        return wall;
    }

    private static FakeLayoutPublicationLookup AllPublishedIn(FabIdentifier fab, params LayoutIdentifier[] scenes)
    {
        FakeLayoutPublicationLookup lookup = new();
        foreach (var scene in scenes)
        {
            lookup.WithLayout(scene, fab);
        }

        return lookup;
    }

    private static void ShouldHaveLogged(
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger,
        LogLevel level,
        string message)
    {
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(level);
        entry.Message.ShouldBe(message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task No_usable_fab_logs_the_wall_rule_and_event_in_their_own_slots(string? fab)
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Layout", b, rule, causingEvent, fab),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {rule}, event {causingEvent}) carries no usable fab; dropping.");
    }

    [Fact]
    public async Task An_unusable_fab_logs_the_fab_text_and_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Layout", b, rule, causingEvent, "NotAFab"),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {rule}, event {causingEvent}) names an unusable fab 'NotAFab'; dropping.");
        logger.Entries[0].Exception.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_unknown_wall_logs_the_wall_rule_and_event_in_their_own_slots()
    {
        InMemoryWallRepository walls = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (unknownWall, rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, new FakeLayoutPublicationLookup(), logger).Handle(
            Request(unknownWall, "Next", null, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested names wall {unknownWall} (rule {rule}, event {causingEvent}), which exists in no fab; dropping.");
    }

    [Fact]
    public async Task An_empty_wall_identifier_logs_the_unknown_wall_text_with_the_rule_and_event_in_their_own_slots()
    {
        InMemoryWallRepository walls = new();
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, new FakeLayoutPublicationLookup(), logger).Handle(
            Request(Guid.Empty, "Next", null, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested names wall {Guid.Empty} (rule {rule}, event {causingEvent}), which exists in no fab; dropping.");
    }

    [Fact]
    public async Task A_wall_in_another_fab_logs_both_fabs_and_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Layout", b, rule, causingEvent, Dresden.Value),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {rule}, event {causingEvent}) names fab '{Dresden}', but the wall belongs to fab '{Munich}'; dropping.");
    }

    [Fact]
    public async Task An_empty_rule_identifier_logs_the_invalid_identifiers_text_with_each_slot_in_place()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var causingEvent = Guid.CreateVersion7();

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Layout", b, Guid.Empty, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {Guid.Empty}, event {causingEvent}) carries an invalid rule or causing-event identifier; dropping.");
    }

    [Fact]
    public async Task An_empty_causing_event_identifier_logs_the_invalid_identifiers_text_with_each_slot_in_place()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var rule = Guid.CreateVersion7();

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Layout", b, rule, Guid.Empty),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {rule}, event {Guid.Empty}) carries an invalid rule or causing-event identifier; dropping.");
    }

    [Fact]
    public async Task A_malformed_target_logs_the_target_and_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Sideways", null, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {rule}, event {causingEvent}) carries a malformed target 'Sideways'; dropping.");
    }

    [Fact]
    public async Task A_target_outside_the_scene_set_logs_the_layout_and_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var outside = NewScene();
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Layout", outside, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {rule}, event {causingEvent}) names layout {outside}, which is not one of the wall's scenes; dropping.");
    }

    [Fact]
    public async Task A_target_that_is_not_Published_logs_the_layout_and_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        var lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich)
            .WithLayout(b, Munich, isPublished: false);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, lookup, logger).Handle(
            Request(wall.Id.Value, "Layout", b, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Warning,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {rule}, event {causingEvent}) names layout {b}, which is not Published; dropping.");
    }

    [Fact]
    public async Task A_dedup_hit_logs_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        FakeDedupStore dedup = new();
        dedup.Reserved.Add((rule, causingEvent));

        await Handler(walls, AllPublishedIn(Munich, a, b), logger, dedup).Handle(
            Request(wall.Id.Value, "Layout", b, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Information,
            $"WallSceneSwitchRequested for wall {wall.Id.Value} (rule {rule}, event {causingEvent}) is a dedup hit; no-op.");
        wall.Showing.ShouldBe(a);
    }

    [Fact]
    public async Task A_real_switch_logs_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Layout", b, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Information,
            $"Switched wall {wall.Id.Value} scene by rule {rule} (event {causingEvent}).");
        wall.Showing.ShouldBe(b);
    }

    [Fact]
    public async Task Already_showing_the_target_logs_a_no_op_with_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, AllPublishedIn(Munich, a, b), logger).Handle(
            Request(wall.Id.Value, "Layout", a, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Information,
            $"Switch request for wall {wall.Id.Value} by rule {rule} (event {causingEvent}) was a no-op; scene unchanged.");
    }

    [Fact]
    public async Task Next_finding_nothing_else_Published_logs_a_no_op_with_the_wall_rule_and_event_in_their_own_slots()
    {
        var (a, b) = (NewScene(), NewScene());
        var wall = NewWall(a, b);
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        var lookup = new FakeLayoutPublicationLookup().WithLayout(a, Munich);
        CapturingLogger<WallSceneSwitchRequestedV1Handler> logger = new();
        var (rule, causingEvent) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Handler(walls, lookup, logger).Handle(
            Request(wall.Id.Value, "Next", null, rule, causingEvent),
            CancellationToken.None);

        ShouldHaveLogged(
            logger,
            LogLevel.Information,
            $"Switch request for wall {wall.Id.Value} by rule {rule} (event {causingEvent}) was a no-op; scene unchanged.");
    }
}
