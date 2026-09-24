using System.Globalization;
using System.Text.Json;
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
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.EventHandlers;

public class FabEventIngestedV1HandlerTests
{
    private static readonly DateTimeOffset BaseMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    private static FabEventIngestedV1 PlcCycleStart(
        Guid? causing = null, string fab = "munich", string payload = "{\"cycleTime\":27}") =>
        new(
            EventIdentifier: causing ?? Guid.CreateVersion7(),
            Fab: fab,
            Source: "plc",
            Device: "station-4",
            Kind: "PlcCycleStart",
            OccurredAt: BaseMoment,
            IngestedAt: BaseMoment.AddSeconds(0.04),
            Payload: payload,
            Metadata: TestMetadata);

    /// <summary>Depth-<paramref name="depth"/> nested array payload, e.g. depth 2 → "[[1]]".</summary>
    private static string Nested(int depth) => new string('[', depth) + "1" + new string(']', depth);

    private static RuleAggregate ActiveSetVariableRule(
        string predicate, string valueExpression, string fab = "munich", string name = "test-rule")
    {
        RuleAggregate rule = new RuleBuilder()
            .WithFab(fab)
            .WithName(name)
            .WithPredicate(predicate)
            .WithAction(RuleAction.SetVariableValue.From("oeeLine1", valueExpression))
            .WithClock(BaseMoment)
            .Build();
        rule.Publish(new FakeClock(BaseMoment.AddMinutes(1)));
        return rule;
    }

    private static RuleAggregate ActiveHighlightRule(
        string name, Guid overlay, int durationMs, DateTimeOffset createdAt)
    {
        RuleAggregate rule = new RuleBuilder()
            .WithName(name)
            .WithAction(RuleAction.HighlightOverlay.From(overlay, durationMs))
            .WithClock(createdAt)
            .Build();
        rule.Publish(new FakeClock(createdAt.AddMinutes(1)));
        return rule;
    }

    [Fact]
    public async Task Matching_event_publishes_SystemVariableValueRequestedV1_with_the_causing_event_id()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule(
            "$.payload.cycleTime <= 30",
            "100 - $.payload.cycleTime * 2"));

        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = new(
            new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance),
            bus,
            new FakeClock(BaseMoment.AddSeconds(0.05)),
            NullLogger<FabEventIngestedV1Handler>.Instance);

        FabEventIngestedV1 ingested = PlcCycleStart();
        await handler.Handle(ingested, CancellationToken.None);

        SystemVariableValueRequestedV1 published = bus.Published
            .OfType<SystemVariableValueRequestedV1>()
            .ShouldHaveSingleItem();
        published.Name.ShouldBe("oeeLine1");
        published.Value.ShouldBe("46");
        published.CausingEventIdentifier.ShouldBe(ingested.EventIdentifier);
    }

    /// <summary>
    /// Characterises the disposal boundary (US2, spec 243): the coming
    /// refactor moves the parsed <see cref="JsonDocument"/> into a
    /// <c>using</c> that ends before publish. A string read out of the
    /// payload is exactly the case a wrong disposal boundary breaks —
    /// <see cref="System.Text.Json.JsonElement.GetString"/> after the
    /// backing buffer is returned to the pool either throws
    /// <see cref="ObjectDisposedException"/> or reads garbage — so this
    /// pins the value surviving intact before that refactor touches
    /// <c>BuildContext</c>'s structure. Must stay green, unmodified,
    /// through spec 243's T004 and T005.
    /// </summary>
    [Fact]
    public async Task A_string_read_from_the_payload_is_published_intact()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule("$.payload.cycleTime <= 30", "$.payload.station"));

        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = HandlerFor(cache, bus);

        FabEventIngestedV1 ingested = PlcCycleStart(
            payload: "{\"cycleTime\":27,\"station\":\"station-4-east\"}");
        await handler.Handle(ingested, CancellationToken.None);

        SystemVariableValueRequestedV1 published = bus.Published
            .OfType<SystemVariableValueRequestedV1>()
            .ShouldHaveSingleItem();
        published.Value.ShouldBe("station-4-east");
    }

    [Fact]
    public async Task HighlightOverlay_action_publishes_OverlayHighlightRequestedV1()
    {
        Guid overlay = Guid.CreateVersion7();
        InMemoryRuleCache cache = new();

        RuleAggregate rule = new RuleBuilder()
            .WithName("highlight-rule")
            .WithAction(RuleAction.HighlightOverlay.From(overlay, 10_000))
            .WithClock(BaseMoment)
            .Build();
        rule.Publish(new FakeClock(BaseMoment.AddMinutes(1)));
        cache.Upsert(rule);

        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = new(
            new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance),
            bus,
            new FakeClock(BaseMoment.AddSeconds(0.05)),
            NullLogger<FabEventIngestedV1Handler>.Instance);

        await handler.Handle(PlcCycleStart(), CancellationToken.None);

        OverlayHighlightRequestedV1 published = bus.Published
            .OfType<OverlayHighlightRequestedV1>()
            .ShouldHaveSingleItem();
        published.OverlayIdentifier.ShouldBe(overlay);
        published.DurationMs.ShouldBe(10_000);
    }

    [Fact]
    public async Task Two_highlight_actions_on_the_same_overlay_both_publish()
    {
        Guid overlay = Guid.CreateVersion7();
        InMemoryRuleCache cache = new();
        // Both survive the upsert: InMemoryRuleCache removes by rule identifier,
        // and Rule.Create mints a fresh one per Build. The names are only labels.
        // The five-minute gap is load-bearing, not decoration: the cache orders
        // the bucket with an unstable List.Sort on CreatedAt (FR-012), so equal
        // moments would make the sequence asserted below non-deterministic.
        cache.Upsert(ActiveHighlightRule("highlight-rule-a", overlay, 5_000, BaseMoment));
        cache.Upsert(ActiveHighlightRule(
            "highlight-rule-b", overlay, 12_000, BaseMoment.AddMinutes(5)));

        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = HandlerFor(cache, bus);

        FabEventIngestedV1 ingested = PlcCycleStart();
        await handler.Handle(ingested, CancellationToken.None);

        // Both windows ride the bus; the kiosk ORs them by later expiry
        // (CellPage.test.tsx:491), so the producer emits both rather than
        // picking. The shared CausingEventIdentifier is what tells "two
        // rules, one event" from "one rule, two events".
        OverlayHighlightRequestedV1[] published = bus.Published
            .OfType<OverlayHighlightRequestedV1>().ToArray();
        published.Length.ShouldBe(2);
        published.Select(p => p.OverlayIdentifier).ShouldBe([overlay, overlay]);
        published.Select(p => p.DurationMs).ShouldBe([5_000, 12_000]);
        published.Select(p => p.CausingEventIdentifier)
            .ShouldBe([ingested.EventIdentifier, ingested.EventIdentifier]);
    }

    [Fact]
    public async Task No_matching_rule_publishes_nothing()
    {
        InMemoryRuleCache cache = new();
        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = new(
            new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance),
            bus,
            new FakeClock(BaseMoment),
            NullLogger<FabEventIngestedV1Handler>.Instance);

        await handler.Handle(PlcCycleStart(), CancellationToken.None);

        bus.Published.ShouldBeEmpty();
    }

    // ---- spec 013: the handler acts only on the originating fab (#1252) ----
    //
    // These assert the *published messages*, not that evaluation returned
    // empty. A handler that scoped evaluation correctly and then published
    // anyway would pass the weaker check while still writing another fab's
    // value — and a published SystemVariableValueRequestedV1 is what actually
    // changes state downstream.

    [Fact]
    public async Task An_event_publishes_nothing_for_a_rule_belonging_to_another_fab()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule(
            "$.payload.cycleTime <= 30", "1", fab: "dresden", name: "dresden-rule"));

        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = HandlerFor(cache, bus);

        await handler.Handle(PlcCycleStart(fab: "munich"), CancellationToken.None);

        bus.Published.ShouldBeEmpty();

        // The bus stays empty for *any* lookup key that misses, so the line
        // above cannot tell "the dresden rule was correctly skipped" from "the
        // handler asked for a fab nobody has rules in". The key it asked for
        // can (#2151): it must be the fab the event came from.
        cache.Lookups.ShouldBe([("munich", "plc", "PlcCycleStart")]);
    }

    [Fact]
    public async Task Only_the_originating_fabs_rule_produces_a_published_change()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule(
            "$.payload.cycleTime <= 30", "100 - $.payload.cycleTime * 2",
            fab: "munich", name: "munich-rule"));
        cache.Upsert(ActiveSetVariableRule(
            "$.payload.cycleTime <= 30", "999", fab: "dresden", name: "dresden-rule"));

        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = HandlerFor(cache, bus);

        await handler.Handle(PlcCycleStart(fab: "munich"), CancellationToken.None);

        SystemVariableValueRequestedV1 published = bus.Published
            .OfType<SystemVariableValueRequestedV1>()
            .ShouldHaveSingleItem();
        published.Value.ShouldBe("46");
        // 999 is the dresden rule's value; seeing it here would mean another
        // fab's automation decided a munich variable.
        published.Value.ShouldNotBe("999");
    }

    [Fact]
    public async Task A_change_is_attributed_to_the_fab_the_event_came_from()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule(
            "$.payload.cycleTime <= 30", "1", fab: "dresden", name: "dresden-rule"));

        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = HandlerFor(cache, bus);

        await handler.Handle(PlcCycleStart(fab: "dresden"), CancellationToken.None);

        SystemVariableValueRequestedV1 published = bus.Published
            .OfType<SystemVariableValueRequestedV1>()
            .ShouldHaveSingleItem();
        published.Metadata.Fab.ShouldBe("dresden");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotAFab")]
    public async Task An_event_without_a_usable_fab_publishes_nothing(string fab)
    {
        // Fails closed. Falling back to evaluating everything is the defect
        // itself, so an unusable fab must trigger nothing rather than all.
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule("$.payload.cycleTime <= 30", "1"));

        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = HandlerFor(cache, bus);

        await handler.Handle(PlcCycleStart(fab: fab), CancellationToken.None);

        bus.Published.ShouldBeEmpty();

        // "Triggered nothing rather than all" has exactly one representation at
        // this seam: the handler never asked. A fallback that carried on under
        // some substitute fab publishes nothing too — the substitute's bucket
        // is empty — so the line above is green for the defect it names
        // (#2151). An empty lookup log is not.
        cache.Lookups.ShouldBeEmpty(
            "an unusable fab must stop the handler before it consults the rule "
            + "cache at all; falling back to any other fab is the #1252 shape");
    }

    /// <summary>
    /// A fab that will not parse silences every rule for that fab, and the
    /// handler fails closed either way — so "published nothing" cannot tell a
    /// diagnosable failure from a silent one. The value has to reach the log,
    /// or the only way to find this is to notice automation has stopped.
    /// </summary>
    [Fact]
    public async Task An_unparseable_fab_is_logged_with_the_value_that_failed()
    {
        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        FabEventIngestedV1Handler handler = new(
            new RuleEvaluator(new InMemoryRuleCache(), NullLogger<RuleEvaluator>.Instance),
            new FakeEventBus(),
            new FakeClock(BaseMoment),
            logger);

        await handler.Handle(PlcCycleStart(fab: "NotAFab"), CancellationToken.None);

        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("NotAFab");
        entry.Exception.ShouldBeOfType<ArgumentException>();
    }

    /// <summary>
    /// The other half: an event that carries no fab at all is a publisher not
    /// stamping one, which is a different problem with a different fix, so it
    /// must not share a message with the case above.
    /// </summary>
    [Fact]
    public async Task An_absent_fab_is_logged_distinctly_from_one_that_will_not_parse()
    {
        CapturingLogger<FabEventIngestedV1Handler> absent = new();
        CapturingLogger<FabEventIngestedV1Handler> unparseable = new();

        // The same event identifier on both, or the rendered messages differ
        // on the id alone and this passes however identical the templates are.
        Guid causing = Guid.CreateVersion7();
        await HandlerWith(absent).Handle(PlcCycleStart(causing, ""), CancellationToken.None);
        await HandlerWith(unparseable).Handle(PlcCycleStart(causing, "NotAFab"), CancellationToken.None);

        absent.Entries.ShouldHaveSingleItem().Message
            .ShouldNotBe(unparseable.Entries.ShouldHaveSingleItem().Message);
    }

    // ---- spec 243 US1: a payload that cannot become an evaluation context
    // costs that event's evaluation, not a dead-letter; a legal one always
    // becomes one (#2496) ----

    /// <summary>
    /// The critical discriminator (plan.md §*Test plan*): a catch-only fix
    /// would make this red by skipping a legal depth-64 payload rather than
    /// green by evaluating it, so the assertion checks that evaluation
    /// actually happened — a published effect, not merely "no exception".
    /// </summary>
    [Fact]
    public async Task A_payload_nested_to_the_depth_ingestion_accepts_is_evaluated()
    {
        const int depth = 64;
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule("$.source == \"plc\"", "1"));

        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = new(
            new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance),
            bus,
            new FakeClock(BaseMoment.AddSeconds(0.05)),
            logger);

        FabEventIngestedV1 ingested = PlcCycleStart(payload: Nested(depth));
        await handler.Handle(ingested, CancellationToken.None);

        SystemVariableValueRequestedV1 published = bus.Published
            .OfType<SystemVariableValueRequestedV1>()
            .ShouldHaveSingleItem();
        published.Name.ShouldBe("oeeLine1");
        published.Value.ShouldBe("1");
        logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_payload_one_level_deeper_than_ingestion_accepts_is_logged_and_skipped()
    {
        const int depth = 65;
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule("$.source == \"plc\"", "1"));

        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = new(
            new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance),
            bus,
            new FakeClock(BaseMoment.AddSeconds(0.05)),
            logger);

        FabEventIngestedV1 ingested = PlcCycleStart(payload: Nested(depth));
        await handler.Handle(ingested, CancellationToken.None);

        bus.Published.ShouldBeEmpty();
        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeAssignableTo<JsonException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("{\"a\":1}}")]
    public async Task A_payload_that_is_not_json_is_logged_and_skipped(string payload)
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule("$.source == \"plc\"", "1"));

        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = new(
            new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance),
            bus,
            new FakeClock(BaseMoment.AddSeconds(0.05)),
            logger);

        FabEventIngestedV1 ingested = PlcCycleStart(payload: payload);
        await handler.Handle(ingested, CancellationToken.None);

        bus.Published.ShouldBeEmpty();
        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeAssignableTo<JsonException>();
        entry.Message.ShouldContain(ingested.EventIdentifier.ToString());
    }

    /// <summary>A contract violation, exercised with <c>null!</c> on purpose (plan.md).</summary>
    [Fact]
    public async Task A_null_payload_is_logged_and_skipped()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveSetVariableRule("$.source == \"plc\"", "1"));

        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        FakeEventBus bus = new();
        FabEventIngestedV1Handler handler = new(
            new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance),
            bus,
            new FakeClock(BaseMoment.AddSeconds(0.05)),
            logger);

        FabEventIngestedV1 ingested = PlcCycleStart(payload: null!);
        await handler.Handle(ingested, CancellationToken.None);

        bus.Published.ShouldBeEmpty();
        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeAssignableTo<JsonException>();
        entry.Message.ShouldContain(ingested.EventIdentifier.ToString());
    }

    /// <summary>
    /// Same-identifier discipline as <see cref="An_absent_fab_is_logged_distinctly_from_one_that_will_not_parse"/>:
    /// one shared event identifier across all three, or the messages differ
    /// on the identifier alone regardless of how similar the templates are.
    /// </summary>
    [Fact]
    public async Task An_unparseable_payload_is_logged_distinctly_from_both_fab_failures()
    {
        CapturingLogger<FabEventIngestedV1Handler> absentFab = new();
        CapturingLogger<FabEventIngestedV1Handler> unparseableFab = new();
        CapturingLogger<FabEventIngestedV1Handler> unparseablePayload = new();

        Guid causing = Guid.CreateVersion7();
        await HandlerWith(absentFab).Handle(PlcCycleStart(causing, ""), CancellationToken.None);
        await HandlerWith(unparseableFab).Handle(PlcCycleStart(causing, "NotAFab"), CancellationToken.None);
        await HandlerWith(unparseablePayload).Handle(
            PlcCycleStart(causing, "munich", "not json"), CancellationToken.None);

        string absentFabMessage = absentFab.Entries.ShouldHaveSingleItem().Message;
        string unparseableFabMessage = unparseableFab.Entries.ShouldHaveSingleItem().Message;
        string unparseablePayloadMessage = unparseablePayload.Entries.ShouldHaveSingleItem().Message;

        unparseablePayloadMessage.ShouldNotBe(absentFabMessage);
        unparseablePayloadMessage.ShouldNotBe(unparseableFabMessage);
    }

    [Fact]
    public async Task An_unparseable_payload_log_carries_its_length_not_its_text()
    {
        const int length = 60_000;
        string payload = "not json".PadRight(length, 'x');

        CapturingLogger<FabEventIngestedV1Handler> logger = new();
        FabEventIngestedV1Handler handler = HandlerWith(logger);

        await handler.Handle(PlcCycleStart(payload: payload), CancellationToken.None);

        string message = logger.Entries.ShouldHaveSingleItem().Message;
        message.ShouldNotContain(payload[..200]);
        message.ShouldContain(length.ToString(CultureInfo.InvariantCulture));
    }

    private static FabEventIngestedV1Handler HandlerWith(ILogger<FabEventIngestedV1Handler> logger) =>
        new(new RuleEvaluator(new InMemoryRuleCache(), NullLogger<RuleEvaluator>.Instance),
            new FakeEventBus(),
            new FakeClock(BaseMoment),
            logger);

    private static FabEventIngestedV1Handler HandlerFor(InMemoryRuleCache cache, FakeEventBus bus) =>
        new(new RuleEvaluator(cache, NullLogger<RuleEvaluator>.Instance),
            bus,
            new FakeClock(BaseMoment.AddSeconds(0.05)),
            NullLogger<FabEventIngestedV1Handler>.Instance);
}
