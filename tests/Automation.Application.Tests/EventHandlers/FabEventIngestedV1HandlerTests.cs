using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Application.EventHandlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
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

    private static FabEventIngestedV1 PlcCycleStart(Guid? causing = null) =>
        new(
            EventIdentifier: causing ?? Guid.CreateVersion7(),
            Fab: "munich",
            Source: "plc",
            Device: "station-4",
            Kind: "PlcCycleStart",
            OccurredAt: BaseMoment,
            IngestedAt: BaseMoment.AddSeconds(0.04),
            Payload: "{\"cycleTime\":27}");

    private static RuleAggregate ActiveSetVariableRule(string predicate, string valueExpression)
    {
        RuleAggregate rule = RuleAggregate.Create(
            RuleName.From("test-rule"),
            "plc",
            "PlcCycleStart",
            RulePredicate.From(predicate),
            RuleAction.SetVariableValue.From("oeeLine1", valueExpression),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(BaseMoment));
        rule.Publish(new FakeClock(BaseMoment.AddMinutes(1)));
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

    [Fact]
    public async Task HighlightOverlay_action_publishes_OverlayHighlightRequestedV1()
    {
        Guid overlay = Guid.CreateVersion7();
        InMemoryRuleCache cache = new();

        RuleAggregate rule = RuleAggregate.Create(
            RuleName.From("highlight-rule"),
            "plc", "PlcCycleStart",
            RulePredicate.From("$.payload.cycleTime <= 30"),
            RuleAction.HighlightOverlay.From(overlay, 10_000),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(BaseMoment));
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
}
