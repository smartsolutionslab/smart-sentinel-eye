using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Application.EventHandlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.EventHandlers;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>FabEventIngestedV1Handler.Handle</c> is split into
/// phases. This is the event-to-overlay-state leg (constitution section IV),
/// and what the split must not move is observable only at the bus: the order
/// of the messages, the contract type each one is published as (Wolverine
/// routes on <c>typeof(TEvent)</c>, so <c>PublishAsync&lt;object&gt;</c> would
/// compile and silently route nowhere), the one clock read they share, the raw
/// fab string in their metadata, and a fresh identifier per message.
/// <c>FabEventIngestedV1HandlerTests</c> uses a bus that stores the instances
/// and cannot tell any of that apart.
/// </summary>
public class FabEventFanOutCharacterisationTests
{
    private static readonly DateTimeOffset BaseMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset IngestedMoment = BaseMoment.AddSeconds(0.04);

    private static readonly DateTimeOffset ClockMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:05Z", CultureInfo.InvariantCulture);

    private static readonly Guid CausingEvent = Guid.Parse("00000000-0000-7000-8000-0000000000e1");

    private static readonly Guid Overlay = Guid.Parse("00000000-0000-7000-8000-0000000000a1");

    private static readonly Guid Wall = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static readonly Guid Layout = Guid.Parse("00000000-0000-7000-8000-0000000000c1");

    private static readonly EventMetadata IncomingMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        BaseMoment,
        null,
        null);

    private static FabEventIngestedV1 EventFor(
        string fab = "munich",
        string payload = "{\"cycleTime\":27}") =>
        new(
            EventIdentifier: CausingEvent,
            Fab: fab,
            Source: "plc",
            Device: "station-4",
            Kind: "PlcCycleStart",
            OccurredAt: BaseMoment,
            IngestedAt: IngestedMoment,
            Payload: payload,
            Metadata: IncomingMetadata);

    private static RuleAggregate ActiveRule(int minutesAfterBase, RuleAction action, string fab = "munich")
    {
        var createdAt = BaseMoment.AddMinutes(minutesAfterBase);
        var rule = new RuleBuilder()
            .WithFab(fab)
            .WithName($"rule-{minutesAfterBase}")
            .WithAction(action)
            .WithClock(createdAt)
            .Build();
        rule.Publish(new FakeClock(createdAt.AddSeconds(1)));
        return rule;
    }

    private static InMemoryRuleCache CacheWithThreeRules()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(0, RuleAction.HighlightOverlay.From(Overlay, 5_000)));
        cache.Upsert(ActiveRule(1, RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2")));
        cache.Upsert(ActiveRule(2, RuleAction.SwitchWallScene.From(Wall, "Layout", Layout)));
        return cache;
    }

    private static FabEventIngestedV1Handler HandlerFor(
        InMemoryRuleCache cache,
        IEventBus bus,
        IClock clock,
        ILogger<FabEventIngestedV1Handler> logger) =>
        new(new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance), bus, clock, logger);

    [Fact]
    public async Task One_event_matching_three_rules_publishes_three_messages_in_effect_order_typed_as_their_contracts()
    {
        RecordingEventBus bus = new();
        CountingClock clock = new(ClockMoment);
        var handler = HandlerFor(CacheWithThreeRules(), bus, clock, NullLogger<FabEventIngestedV1Handler>.Instance);

        await handler.Handle(EventFor(), CancellationToken.None);

        bus.Calls.Select(call => call.EventType).ShouldBe(
        [
            typeof(OverlayHighlightRequestedV1),
            typeof(SystemVariableValueRequestedV1),
            typeof(WallSceneSwitchRequestedV1),
        ]);
        var highlight = bus.Calls[0].Message.ShouldBeOfType<OverlayHighlightRequestedV1>();
        highlight.OverlayIdentifier.ShouldBe(Overlay);
        highlight.DurationMs.ShouldBe(5_000);
        highlight.RequestedAt.ShouldBe(ClockMoment);
        highlight.CausingEventIdentifier.ShouldBe(CausingEvent);
        var variable = bus.Calls[1].Message.ShouldBeOfType<SystemVariableValueRequestedV1>();
        variable.Name.ShouldBe("oeeLine1");
        variable.Value.ShouldBe("46");
        variable.RequestedAt.ShouldBe(ClockMoment);
        variable.CausingEventIdentifier.ShouldBe(CausingEvent);
        var wallSwitch = bus.Calls[2].Message.ShouldBeOfType<WallSceneSwitchRequestedV1>();
        wallSwitch.Wall.ShouldBe(Wall);
        wallSwitch.Target.ShouldBe("Layout");
        wallSwitch.TargetLayout.ShouldBe(Layout);
        wallSwitch.RequestedAt.ShouldBe(ClockMoment);
        wallSwitch.CausingEventIdentifier.ShouldBe(CausingEvent);
    }

    [Fact]
    public async Task Every_message_carries_the_raw_fab_the_ingested_at_a_null_actor_and_its_own_fresh_identifier()
    {
        RecordingEventBus bus = new();
        var handler = HandlerFor(
            CacheWithThreeRules(),
            bus,
            new CountingClock(ClockMoment),
            NullLogger<FabEventIngestedV1Handler>.Instance);

        await handler.Handle(EventFor(), CancellationToken.None);

        var metadata = bus.Calls
            .Select(call => call.Message switch
            {
                OverlayHighlightRequestedV1 message => message.Metadata,
                SystemVariableValueRequestedV1 message => message.Metadata,
                WallSceneSwitchRequestedV1 message => message.Metadata,
                _ => throw new InvalidOperationException("unexpected message"),
            })
            .ToArray();
        metadata.Length.ShouldBe(3);
        foreach (var each in metadata)
        {
            each.Fab.ShouldBe("munich");
            each.RootIngestedAt.ShouldBe(IngestedMoment);
            each.OccurredAt.ShouldBe(ClockMoment);
            each.Actor.ShouldBeNull();
            each.EventIdentifier.ShouldNotBe(Guid.Empty);
            each.EventIdentifier.ShouldNotBe(CausingEvent);
            each.EventIdentifier.Version.ShouldBe(7);
        }

        metadata.Select(each => each.EventIdentifier).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task The_clock_is_read_once_per_event_that_has_effects_and_the_fan_out_is_logged_at_debug()
    {
        RecordingEventBus bus = new();
        CountingClock clock = new(ClockMoment);
        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        var handler = HandlerFor(CacheWithThreeRules(), bus, clock, logger);

        await handler.Handle(EventFor(), CancellationToken.None);

        clock.Reads.ShouldBe(1);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Debug);
        logger.Entries[0].Message.ShouldBe(
            "Fanned out 3 action(s) for 00000000-0000-7000-8000-0000000000e1 (plc/PlcCycleStart).");
        logger.Entries[0].Exception.ShouldBeNull();
    }

    [Fact]
    public async Task An_event_that_matches_no_rule_publishes_nothing_reads_no_clock_and_logs_nothing()
    {
        RecordingEventBus bus = new();
        CountingClock clock = new(ClockMoment);
        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        var handler = HandlerFor(new InMemoryRuleCache(), bus, clock, logger);

        await handler.Handle(EventFor(), CancellationToken.None);

        bus.Calls.ShouldBeEmpty();
        clock.Reads.ShouldBe(0);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_event_from_another_fab_publishes_nothing_and_looks_up_only_its_own_fab()
    {
        var cache = CacheWithThreeRules();
        RecordingEventBus bus = new();
        var handler = HandlerFor(
            cache,
            bus,
            new CountingClock(ClockMoment),
            NullLogger<FabEventIngestedV1Handler>.Instance);

        await handler.Handle(EventFor(fab: "dresden"), CancellationToken.None);

        bus.Calls.ShouldBeEmpty();
        cache.Lookups.ShouldBe([("dresden", "plc", "PlcCycleStart")]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task An_event_without_a_fab_is_skipped_with_a_warning_before_any_lookup(string? fab)
    {
        var cache = CacheWithThreeRules();
        RecordingEventBus bus = new();
        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        var handler = HandlerFor(cache, bus, new CountingClock(ClockMoment), logger);

        await handler.Handle(EventFor(fab: fab!), CancellationToken.None);

        bus.Calls.ShouldBeEmpty();
        cache.Lookups.ShouldBeEmpty();
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldBe(
            "Ingested event 00000000-0000-7000-8000-0000000000e1 carried no fab; no rule evaluated.");
        logger.Entries[0].Exception.ShouldBeNull();
    }

    [Fact]
    public async Task An_event_whose_fab_will_not_parse_is_skipped_with_a_warning_that_names_the_value()
    {
        var cache = CacheWithThreeRules();
        RecordingEventBus bus = new();
        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        var handler = HandlerFor(cache, bus, new CountingClock(ClockMoment), logger);

        await handler.Handle(EventFor(fab: "Munich"), CancellationToken.None);

        bus.Calls.ShouldBeEmpty();
        cache.Lookups.ShouldBeEmpty();
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldBe(
            "Ingested event 00000000-0000-7000-8000-0000000000e1 carried fab 'Munich', which is not a valid fab identifier; no rule evaluated.");
        logger.Entries[0].Exception.ShouldBeAssignableTo<ArgumentException>();
    }

    [Theory]
    [InlineData("{not json", 9)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public async Task An_event_whose_payload_will_not_parse_is_skipped_with_a_warning_naming_its_length(
        string? payload,
        int length)
    {
        var cache = CacheWithThreeRules();
        RecordingEventBus bus = new();
        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        var handler = HandlerFor(cache, bus, new CountingClock(ClockMoment), logger);

        await handler.Handle(EventFor(payload: payload!), CancellationToken.None);

        bus.Calls.ShouldBeEmpty();
        cache.Lookups.ShouldBeEmpty();
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldBe(
            $"Ingested event 00000000-0000-7000-8000-0000000000e1 carried a payload of {length} characters that could not be parsed (not valid JSON, or nested too deeply); no rule evaluated.");
        logger.Entries[0].Exception.ShouldBeAssignableTo<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task A_rule_that_cannot_evaluate_costs_the_event_nothing_else()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(0, RuleAction.SetVariableValue.From("broken", "1 / 0")));
        cache.Upsert(ActiveRule(1, RuleAction.HighlightOverlay.From(Overlay, 2_000)));
        RecordingEventBus bus = new();
        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        var handler = HandlerFor(cache, bus, new CountingClock(ClockMoment), logger);

        await handler.Handle(EventFor(), CancellationToken.None);

        bus.Calls.Select(call => call.EventType).ShouldBe([typeof(OverlayHighlightRequestedV1)]);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Message.ShouldBe(
            "Fanned out 1 action(s) for 00000000-0000-7000-8000-0000000000e1 (plc/PlcCycleStart).");
    }

    private sealed class RecordingEventBus : IEventBus
    {
        private readonly List<(Type EventType, object Message)> calls = [];

        public List<(Type EventType, object Message)> Calls => calls;

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            calls.Add((typeof(TEvent), integrationEvent));
            return Task.CompletedTask;
        }
    }

    private sealed class CountingClock(DateTimeOffset utcNow) : IClock
    {
        public int Reads { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Reads++;
                return utcNow.AddSeconds(Reads - 1);
            }
        }
    }
}
