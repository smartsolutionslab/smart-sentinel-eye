using System.Globalization;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Application.Queries;
using SmartSentinelEye.Automation.Application.Queries.Handlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Queries;

/// <summary>Spec 007 T091 — the three read-side query handlers.</summary>
public class RuleQueryHandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private const string Sample =
        """{"source":"plc","kind":"PlcCycleStart","device":"press-1","payload":{"cycleTime":20}}""";

    private static (InMemoryRuleRepository Repository, IRuleQuerySource Source) Seed(params RuleAggregate[] rules)
    {
        InMemoryRuleRepository repository = new();
        foreach (RuleAggregate rule in rules)
        {
            repository.Add(rule);
        }
        return (repository, new InMemoryRuleQuerySource(repository));
    }

    // ---- GetRuleQuery ----

    [Fact]
    public async Task Get_returns_the_rule_when_the_name_matches()
    {
        RuleAggregate rule = new RuleBuilder().WithName("high-oee").WithClock(Moment).Build();
        (_, IRuleQuerySource source) = Seed(rule);

        Result<RuleDto, GetRuleError> result =
            await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery("high-oee"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("high-oee");
        result.Value.State.ShouldBe(RuleState.Draft.Value);
        result.Value.PublishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Get_returns_RuleNotFound_for_an_unknown_name()
    {
        (_, IRuleQuerySource source) = Seed(new RuleBuilder().WithName("known").Build());

        Result<RuleDto, GetRuleError> result =
            await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery("missing"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<GetRuleError.RuleNotFound>();
    }

    [Fact]
    public async Task Get_projects_the_predicate_as_its_raw_AEL_string()
    {
        RuleAggregate rule = new RuleBuilder()
            .WithName("cycle")
            .WithPredicate("$.payload.cycleTime <= 30")
            .Build();
        (_, IRuleQuerySource source) = Seed(rule);

        Result<RuleDto, GetRuleError> result =
            await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery("cycle"), CancellationToken.None);

        result.Value.Predicate.ShouldBe("$.payload.cycleTime <= 30");
    }

    [Fact]
    public async Task Get_projects_a_SetVariableValue_action_as_its_tagged_wire_shape()
    {
        RuleAggregate rule = new RuleBuilder()
            .WithName("set-var")
            .WithAction(RuleAction.SetVariableValue.From("oeeLine1", "42"))
            .Build();
        (_, IRuleQuerySource source) = Seed(rule);

        RuleActionDto action =
            (await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery("set-var"), CancellationToken.None))
            .Value.Action;

        action.Kind.ShouldBe(RuleActionDto.SetVariableValueKind);
        action.VariableName.ShouldBe("oeeLine1");
        action.ValueExpression.ShouldBe("42");
        action.Overlay.ShouldBeNull();
        action.DurationMs.ShouldBeNull();
    }

    [Fact]
    public async Task Get_projects_a_HighlightOverlay_action_as_its_tagged_wire_shape()
    {
        Guid overlay = Guid.CreateVersion7();
        RuleAggregate rule = new RuleBuilder()
            .WithName("highlight")
            .WithAction(RuleAction.HighlightOverlay.From(overlay, 5_000))
            .Build();
        (_, IRuleQuerySource source) = Seed(rule);

        RuleActionDto action =
            (await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery("highlight"), CancellationToken.None))
            .Value.Action;

        action.Kind.ShouldBe(RuleActionDto.HighlightOverlayKind);
        action.Overlay.ShouldBe(overlay);
        action.DurationMs.ShouldBe(5_000);
        action.VariableName.ShouldBeNull();
        action.ValueExpression.ShouldBeNull();
    }

    // ---- ListRulesQuery ----

    [Fact]
    public async Task List_without_filters_returns_every_rule_newest_first()
    {
        RuleAggregate older = new RuleBuilder().WithName("older").WithClock(Moment).Build();
        RuleAggregate newer = new RuleBuilder().WithName("newer").WithClock(Moment.AddHours(1)).Build();
        (_, IRuleQuerySource source) = Seed(older, newer);

        Result<IReadOnlyList<RuleDto>, ListRulesError> result =
            await new ListRulesQueryHandler(source).HandleAsync(
                new ListRulesQuery(null, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value[0].Name.ShouldBe("newer");
    }

    [Fact]
    public async Task List_filters_by_state()
    {
        RuleAggregate draft = new RuleBuilder().WithName("draft-rule").WithClock(Moment).Build();
        RuleAggregate active = new RuleBuilder().WithName("active-rule").WithClock(Moment).Build();
        active.Publish(new FakeClock(Moment));
        (_, IRuleQuerySource source) = Seed(draft, active);

        Result<IReadOnlyList<RuleDto>, ListRulesError> result =
            await new ListRulesQueryHandler(source).HandleAsync(
                new ListRulesQuery(RuleState.Active.Value, null, null), CancellationToken.None);

        result.Value.Count.ShouldBe(1);
        result.Value[0].Name.ShouldBe("active-rule");
    }

    [Fact]
    public async Task List_filters_by_trigger_source_and_kind()
    {
        RuleAggregate plc = new RuleBuilder().WithName("plc-rule")
            .WithTriggerSource("plc").WithTriggerKind("PlcCycleStart").Build();
        RuleAggregate inference = new RuleBuilder().WithName("inference-rule")
            .WithTriggerSource("inference").WithTriggerKind("DefectDetected").Build();
        (_, IRuleQuerySource source) = Seed(plc, inference);

        Result<IReadOnlyList<RuleDto>, ListRulesError> bySource =
            await new ListRulesQueryHandler(source).HandleAsync(
                new ListRulesQuery(null, "inference", null), CancellationToken.None);
        bySource.Value.Count.ShouldBe(1);
        bySource.Value[0].Name.ShouldBe("inference-rule");

        Result<IReadOnlyList<RuleDto>, ListRulesError> byKind =
            await new ListRulesQueryHandler(source).HandleAsync(
                new ListRulesQuery(null, null, "PlcCycleStart"), CancellationToken.None);
        byKind.Value.Count.ShouldBe(1);
        byKind.Value[0].Name.ShouldBe("plc-rule");
    }

    [Fact]
    public async Task List_rejects_an_unknown_state_filter_rather_than_ignoring_it()
    {
        (_, IRuleQuerySource source) = Seed(new RuleBuilder().Build());

        Result<IReadOnlyList<RuleDto>, ListRulesError> result =
            await new ListRulesQueryHandler(source).HandleAsync(
                new ListRulesQuery("Retired", null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<ListRulesError.InvalidState>();
    }

    // ---- DryRunRuleQuery ----

    [Fact]
    public async Task DryRun_reports_a_match_and_the_value_the_action_would_write()
    {
        RuleAggregate rule = new RuleBuilder()
            .WithName("cycle")
            .WithPredicate("$.payload.cycleTime <= 30")
            .WithAction(RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2"))
            .Build();
        (_, IRuleQuerySource source) = Seed(rule);

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery("cycle", Sample), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Matched.ShouldBeTrue();
        result.Value.EvaluatedValue.ShouldBe("60");
    }

    [Fact]
    public async Task DryRun_reports_no_match_without_an_evaluated_value()
    {
        RuleAggregate rule = new RuleBuilder()
            .WithName("cycle")
            .WithPredicate("$.payload.cycleTime > 100")
            .Build();
        (_, IRuleQuerySource source) = Seed(rule);

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery("cycle", Sample), CancellationToken.None);

        result.Value.Matched.ShouldBeFalse();
        result.Value.EvaluatedValue.ShouldBeNull();
    }

    [Fact]
    public async Task DryRun_matches_a_HighlightOverlay_rule_with_no_value_to_evaluate()
    {
        RuleAggregate rule = new RuleBuilder()
            .WithName("highlight")
            .WithPredicate("$.payload.cycleTime <= 30")
            .WithAction(RuleAction.HighlightOverlay.From(Guid.CreateVersion7(), 5_000))
            .Build();
        (_, IRuleQuerySource source) = Seed(rule);

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery("highlight", Sample), CancellationToken.None);

        result.Value.Matched.ShouldBeTrue();
        result.Value.EvaluatedValue.ShouldBeNull();
    }

    [Fact]
    public async Task DryRun_works_on_a_Draft_rule_that_is_not_in_the_cache()
    {
        RuleAggregate draft = new RuleBuilder().WithName("draft-rule").Build();
        draft.State.ShouldBe(RuleState.Draft);
        (_, IRuleQuerySource source) = Seed(draft);

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery("draft-rule", Sample), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Matched.ShouldBeTrue();
    }

    [Fact]
    public async Task DryRun_returns_RuleNotFound_for_an_unknown_name()
    {
        (_, IRuleQuerySource source) = Seed(new RuleBuilder().WithName("known").Build());

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery("missing", Sample), CancellationToken.None);

        result.Error.ShouldBeOfType<DryRunRuleError.RuleNotFound>();
    }

    [Fact]
    public async Task DryRun_rejects_a_sample_that_is_not_JSON()
    {
        (_, IRuleQuerySource source) = Seed(new RuleBuilder().WithName("cycle").Build());

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery("cycle", "not json at all"), CancellationToken.None);

        result.Error.ShouldBeOfType<DryRunRuleError.SampleEventNotJson>();
    }

    [Fact]
    public async Task DryRun_surfaces_an_evaluation_failure_rather_than_reporting_no_match()
    {
        // The field is absent from the sample, so the comparison cannot be
        // evaluated. "Did not match" would be a lie — the rule is broken for
        // this shape of event, which is what a dry run exists to expose.
        RuleAggregate rule = new RuleBuilder()
            .WithName("missing-field")
            .WithPredicate("$.payload.absent <= 30")
            .Build();
        (_, IRuleQuerySource source) = Seed(rule);

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery("missing-field", Sample), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<DryRunRuleError.EvaluationFailed>();
    }
}
