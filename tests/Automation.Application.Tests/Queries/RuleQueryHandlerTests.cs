using System.Globalization;
using System.Net;
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
    // The caller's fabs, not a filter they chose: these suites all act as an
    // operator assigned to munich, which is RuleBuilder's default.
    private static readonly IReadOnlyList<FabIdentifier> Munich = [FabIdentifier.From("munich")];

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

    // Without the version on the read side a caller has nothing to put in
    // If-Match, and the cross-request check degrades to no check (ADR-0113).
    [Fact]
    public async Task Get_returns_the_aggregate_version()
    {
        RuleAggregate rule = new RuleBuilder().WithName("versioned-rule").WithClock(Moment).Build();
        (_, IRuleQuerySource source) = Seed(rule);

        Result<RuleDto, GetRuleError> result = await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Munich, "versioned-rule"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(rule.Version);
    }


    [Fact]
    public async Task Get_returns_the_rule_when_the_name_matches()
    {
        RuleAggregate rule = new RuleBuilder().WithName("high-oee").WithClock(Moment).Build();
        (_, IRuleQuerySource source) = Seed(rule);

        Result<RuleDto, GetRuleError> result =
            await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery(Munich, "high-oee"), CancellationToken.None);

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
            await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery(Munich, "missing"), CancellationToken.None);

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
            await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery(Munich, "cycle"), CancellationToken.None);

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
            (await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery(Munich, "set-var"), CancellationToken.None))
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
            (await new GetRuleQueryHandler(source).HandleAsync(new GetRuleQuery(Munich, "highlight"), CancellationToken.None))
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
                new ListRulesQuery(Munich, null, null, null), CancellationToken.None);

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
                new ListRulesQuery(Munich, RuleState.Active.Value, null, null), CancellationToken.None);

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
                new ListRulesQuery(Munich, null, "inference", null), CancellationToken.None);
        bySource.Value.Count.ShouldBe(1);
        bySource.Value[0].Name.ShouldBe("inference-rule");

        Result<IReadOnlyList<RuleDto>, ListRulesError> byKind =
            await new ListRulesQueryHandler(source).HandleAsync(
                new ListRulesQuery(Munich, null, null, "PlcCycleStart"), CancellationToken.None);
        byKind.Value.Count.ShouldBe(1);
        byKind.Value[0].Name.ShouldBe("plc-rule");
    }

    [Fact]
    public async Task List_rejects_an_unknown_state_filter_rather_than_ignoring_it()
    {
        (_, IRuleQuerySource source) = Seed(new RuleBuilder().Build());

        Result<IReadOnlyList<RuleDto>, ListRulesError> result =
            await new ListRulesQueryHandler(source).HandleAsync(
                new ListRulesQuery(Munich, "Retired", null, null), CancellationToken.None);

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
                new DryRunRuleQuery(Munich, "cycle", Sample), CancellationToken.None);

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
                new DryRunRuleQuery(Munich, "cycle", Sample), CancellationToken.None);

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
                new DryRunRuleQuery(Munich, "highlight", Sample), CancellationToken.None);

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
                new DryRunRuleQuery(Munich, "draft-rule", Sample), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Matched.ShouldBeTrue();
    }

    [Fact]
    public async Task DryRun_returns_RuleNotFound_for_an_unknown_name()
    {
        (_, IRuleQuerySource source) = Seed(new RuleBuilder().WithName("known").Build());

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery(Munich, "missing", Sample), CancellationToken.None);

        result.Error.ShouldBeOfType<DryRunRuleError.RuleNotFound>();
    }

    [Fact]
    public async Task DryRun_rejects_a_sample_that_is_not_JSON()
    {
        (_, IRuleQuerySource source) = Seed(new RuleBuilder().WithName("cycle").Build());

        Result<DryRunResultDto, DryRunRuleError> result =
            await new DryRunRuleQueryHandler(source).HandleAsync(
                new DryRunRuleQuery(Munich, "cycle", "not json at all"), CancellationToken.None);

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
                new DryRunRuleQuery(Munich, "missing-field", Sample), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<DryRunRuleError.EvaluationFailed>();
    }

    // ---- spec 013: reads are scoped to the caller's fabs (FR-005, FR-007) ----

    private static readonly IReadOnlyList<FabIdentifier> Dresden = [FabIdentifier.From("dresden")];

    [Fact]
    public async Task Get_reports_another_fabs_rule_as_not_found()
    {
        RuleAggregate munichRule = new RuleBuilder().WithFab("munich").WithName("secret-rule").WithClock(Moment).Build();
        (_, IRuleQuerySource source) = Seed(munichRule);

        Result<RuleDto, GetRuleError> foreign = await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Dresden, "secret-rule"), CancellationToken.None);
        Result<RuleDto, GetRuleError> absent = await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Dresden, "never-existed"), CancellationToken.None);

        // Identical, deliberately. A distinguishable refusal would confirm the
        // rule exists and let an operator enumerate another fab's names one
        // guess at a time.
        foreign.IsFailure.ShouldBeTrue();
        absent.IsFailure.ShouldBeTrue();
        foreign.Error.Code.ShouldBe(absent.Error.Code);
        foreign.Error.Status.ShouldBe(absent.Error.Status);
    }

    [Fact]
    public async Task List_omits_rules_from_fabs_the_caller_does_not_hold()
    {
        (_, IRuleQuerySource source) = Seed(
            new RuleBuilder().WithFab("munich").WithName("munich-rule").WithClock(Moment).Build(),
            new RuleBuilder().WithFab("dresden").WithName("dresden-rule").WithClock(Moment).Build());

        Result<IReadOnlyList<RuleDto>, ListRulesError> result = await new ListRulesQueryHandler(source)
            .HandleAsync(new ListRulesQuery(Munich, null, null, null), CancellationToken.None);

        result.Value.Select(rule => rule.Name).ShouldBe(["munich-rule"]);
    }

    [Fact]
    public async Task List_spans_every_fab_the_caller_holds()
    {
        (_, IRuleQuerySource source) = Seed(
            new RuleBuilder().WithFab("munich").WithName("munich-rule").WithClock(Moment).Build(),
            new RuleBuilder().WithFab("dresden").WithName("dresden-rule").WithClock(Moment).Build());

        IReadOnlyList<FabIdentifier> both = [FabIdentifier.From("munich"), FabIdentifier.From("dresden")];
        Result<IReadOnlyList<RuleDto>, ListRulesError> result = await new ListRulesQueryHandler(source)
            .HandleAsync(new ListRulesQuery(both, null, null, null), CancellationToken.None);

        result.Value.Select(rule => rule.Name)
            .ShouldBe(["munich-rule", "dresden-rule"], ignoreOrder: true);
    }

    [Fact]
    public async Task An_operator_assigned_to_no_fab_lists_nothing()
    {
        (_, IRuleQuerySource source) = Seed(
            new RuleBuilder().WithFab("munich").WithName("munich-rule").WithClock(Moment).Build());

        Result<IReadOnlyList<RuleDto>, ListRulesError> result = await new ListRulesQueryHandler(source)
            .HandleAsync(new ListRulesQuery([], null, null, null), CancellationToken.None);

        // Empty, not everything. The endpoint refuses this caller outright,
        // but the handler must not fall open if it is ever reached.
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_projected_rule_carries_its_fab()
    {
        (_, IRuleQuerySource source) = Seed(
            new RuleBuilder().WithFab("munich").WithName("tagged").WithClock(Moment).Build());

        Result<RuleDto, GetRuleError> result = await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Munich, "tagged"), CancellationToken.None);

        result.Value.Fab.ShouldBe("munich");
    }

    // ---- a name is unique per fab, so lookup by name alone can be ambiguous ----
    //
    // The_same_rule_name_is_accepted_in_two_fabs asserts this collision is
    // legal. These pin what the by-name reads do when they hit one: refuse and
    // say so, rather than picking a rule the caller did not mean — or, as they
    // did before, throwing out of the handler as a 500.

    private static readonly IReadOnlyList<FabIdentifier> Both =
        [FabIdentifier.From("munich"), FabIdentifier.From("dresden")];

    [Fact]
    public async Task Get_refuses_a_name_that_resolves_in_two_of_the_callers_fabs()
    {
        (_, IRuleQuerySource source) = Seed(
            new RuleBuilder().WithFab("munich").WithName("shared").WithClock(Moment).Build(),
            new RuleBuilder().WithFab("dresden").WithName("shared").WithClock(Moment).Build());

        Result<RuleDto, GetRuleError> result = await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Both, "shared"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetRuleError.FabAmbiguous>();
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);

        // Named, so the caller can retry without guessing. Both are fabs they
        // already hold, so this tells them nothing they could not already see.
        result.Error.Message.ShouldContain("munich");
        result.Error.Message.ShouldContain("dresden");
    }

    [Fact]
    public async Task Get_answers_when_only_one_of_the_callers_fabs_holds_the_name()
    {
        // The refusal above must not become a blanket refusal for multi-fab
        // callers: the ambiguity is the collision, not the second fab.
        (_, IRuleQuerySource source) = Seed(
            new RuleBuilder().WithFab("munich").WithName("munich-only").WithClock(Moment).Build(),
            new RuleBuilder().WithFab("dresden").WithName("dresden-only").WithClock(Moment).Build());

        Result<RuleDto, GetRuleError> result = await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Both, "dresden-only"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task DryRun_refuses_a_name_that_resolves_in_two_of_the_callers_fabs()
    {
        (_, IRuleQuerySource source) = Seed(
            new RuleBuilder().WithFab("munich").WithName("shared").WithPredicate("$.payload.cycleTime <= 30").Build(),
            new RuleBuilder().WithFab("dresden").WithName("shared").WithPredicate("$.payload.cycleTime <= 30").Build());

        Result<DryRunResultDto, DryRunRuleError> result = await new DryRunRuleQueryHandler(source)
            .HandleAsync(new DryRunRuleQuery(Both, "shared", Sample), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<DryRunRuleError.FabAmbiguous>();
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Error.Message.ShouldContain("munich");
        result.Error.Message.ShouldContain("dresden");
    }

    [Fact]
    public async Task DryRun_answers_when_only_one_of_the_callers_fabs_holds_the_name()
    {
        (_, IRuleQuerySource source) = Seed(
            new RuleBuilder().WithFab("dresden").WithName("dresden-only")
                .WithPredicate("$.payload.cycleTime <= 30").Build());

        Result<DryRunResultDto, DryRunRuleError> result = await new DryRunRuleQueryHandler(source)
            .HandleAsync(new DryRunRuleQuery(Both, "dresden-only", Sample), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Matched.ShouldBeTrue();
    }
}
