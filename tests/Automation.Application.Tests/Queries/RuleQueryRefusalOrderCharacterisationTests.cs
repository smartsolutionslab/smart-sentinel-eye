using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Application.Queries;
using SmartSentinelEye.Automation.Application.Queries.Handlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Queries;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before the filters of <c>ListRulesQueryHandler</c> and the
/// lookup and evaluation blocks of <c>DryRunRuleQueryHandler</c> move into
/// helpers. <c>RuleQueryHandlerTests</c> exercises each refusal alone; the
/// order in which several true conditions are reported, the exact words of the
/// answer and the exact text of the evaluation-failure log are what an
/// extraction can move without breaking any of those.
/// </summary>
public class RuleQueryRefusalOrderCharacterisationTests
{
    private static readonly IReadOnlyList<FabIdentifier> Munich = [FabIdentifier.From("munich")];

    private static readonly IReadOnlyList<FabIdentifier> Both =
        [FabIdentifier.From("munich"), FabIdentifier.From("dresden")];

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private const string Sample =
        """{"source":"plc","kind":"PlcCycleStart","device":"press-1","payload":{"cycleTime":20}}""";

    private static InMemoryRuleQuerySource Seed(params RuleAggregate[] rules)
    {
        InMemoryRuleRepository repository = new();
        foreach (var rule in rules)
        {
            repository.Add(rule);
        }

        return new InMemoryRuleQuerySource(repository);
    }

    private static RuleAggregate RuleNamed(
        string name,
        int minutesAfterMoment,
        string source = "plc",
        string kind = "PlcCycleStart",
        string fab = "munich",
        bool publish = false,
        string predicate = "$.payload.cycleTime <= 30",
        RuleAction? action = null)
    {
        var createdAt = Moment.AddMinutes(minutesAfterMoment);
        var rule = new RuleBuilder()
            .WithFab(fab)
            .WithName(name)
            .WithTriggerSource(source)
            .WithTriggerKind(kind)
            .WithPredicate(predicate)
            .WithAction(action ?? RuleAction.HighlightOverlay.From(Guid.CreateVersion7(), 5_000))
            .WithClock(createdAt)
            .Build();
        if (publish)
        {
            rule.Publish(new FakeClock(createdAt.AddSeconds(1)));
        }

        return rule;
    }

    private static async Task<IReadOnlyList<string>> ListedNames(
        IRuleQuerySource source,
        string? state,
        string? triggerSource,
        string? triggerKind)
    {
        var result = await new ListRulesQueryHandler(source).HandleAsync(
            new ListRulesQuery(Munich, state, triggerSource, triggerKind),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        return [.. result.Value.Select(dto => dto.Name)];
    }

    private static InMemoryRuleQuerySource FourRules() =>
        Seed(
            RuleNamed("draft-plc-cycle", 0),
            RuleNamed("active-plc-cycle", 1, publish: true),
            RuleNamed("active-plc-other", 2, kind: "Other", publish: true),
            RuleNamed("active-opc-cycle", 3, source: "opc", publish: true));

    // ---- List ----

    [Fact]
    public async Task List_with_an_invalid_state_and_an_invalid_trigger_source_answers_InvalidState()
    {
        var result = await new ListRulesQueryHandler(FourRules()).HandleAsync(
            new ListRulesQuery(Munich, "Bogus", new string('s', 17), new string('k', 129)),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        var refusal = result.Error.ShouldBeOfType<ListRulesError.InvalidState>();
        refusal.Code.ShouldBe("RULE_INVALID_STATE_FILTER");
        refusal.Status.ShouldBe(HttpStatusCode.BadRequest);
        refusal.Message.ShouldBe("'Bogus' is not a rule state. Expected one of: Draft, Active, Archived.");
    }

    [Fact]
    public async Task List_state_names_are_case_sensitive()
    {
        var result = await new ListRulesQueryHandler(FourRules()).HandleAsync(
            new ListRulesQuery(Munich, "active", null, null),
            CancellationToken.None);

        result.Error.ShouldBeOfType<ListRulesError.InvalidState>().Value.ShouldBe("active");
    }

    [Fact]
    public async Task List_with_a_valid_state_and_an_invalid_trigger_source_is_an_empty_success()
    {
        (await ListedNames(FourRules(), "Active", new string('s', 17), null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task List_with_a_valid_state_and_an_invalid_trigger_kind_is_an_empty_success()
    {
        (await ListedNames(FourRules(), "Active", null, new string('k', 129))).ShouldBeEmpty();
    }

    [Fact]
    public async Task List_with_a_valid_source_and_an_invalid_trigger_kind_is_an_empty_success()
    {
        (await ListedNames(FourRules(), null, "plc", new string('k', 129))).ShouldBeEmpty();
    }

    [Fact]
    public async Task List_with_an_invalid_source_and_an_invalid_kind_is_an_empty_success()
    {
        (await ListedNames(FourRules(), null, new string('s', 17), new string('k', 129))).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData(" ", " ", " ")]
    [InlineData("  ", null, "\t")]
    [InlineData(null, "\r\n", null)]
    public async Task List_ignores_filters_that_are_blank(string? state, string? triggerSource, string? triggerKind)
    {
        (await ListedNames(FourRules(), state, triggerSource, triggerKind)).ShouldBe(
            ["active-opc-cycle", "active-plc-other", "active-plc-cycle", "draft-plc-cycle"]);
    }

    [Theory]
    [InlineData("Active", null, null, new[] { "active-opc-cycle", "active-plc-other", "active-plc-cycle" })]
    [InlineData("Draft", null, null, new[] { "draft-plc-cycle" })]
    [InlineData("Archived", null, null, new string[0])]
    [InlineData(null, "plc", null, new[] { "active-plc-other", "active-plc-cycle", "draft-plc-cycle" })]
    [InlineData(null, "opc", null, new[] { "active-opc-cycle" })]
    [InlineData(null, null, "PlcCycleStart", new[] { "active-opc-cycle", "active-plc-cycle", "draft-plc-cycle" })]
    [InlineData(null, null, "Other", new[] { "active-plc-other" })]
    [InlineData("Active", "plc", null, new[] { "active-plc-other", "active-plc-cycle" })]
    [InlineData("Active", null, "PlcCycleStart", new[] { "active-opc-cycle", "active-plc-cycle" })]
    [InlineData(null, "plc", "PlcCycleStart", new[] { "active-plc-cycle", "draft-plc-cycle" })]
    [InlineData("Active", "plc", "PlcCycleStart", new[] { "active-plc-cycle" })]
    [InlineData("Draft", "opc", null, new string[0])]
    [InlineData(null, "PLC", null, new string[0])]
    public async Task List_combines_the_filters_and_keeps_newest_first(
        string? state,
        string? triggerSource,
        string? triggerKind,
        string[] expected)
    {
        (await ListedNames(FourRules(), state, triggerSource, triggerKind)).ShouldBe(expected);
    }

    // ---- Dry run ----

    private static DryRunRuleQueryHandler DryRunHandler(
        IRuleQuerySource source,
        CapturingLogger<DryRunRuleQueryHandler> logger) =>
        new(source, logger);

    [Theory]
    [InlineData("unknown-rule")]
    [InlineData("UPPER")]
    [InlineData("")]
    [InlineData("a")]
    public async Task DryRun_of_an_unknown_or_illegal_name_with_a_sample_that_is_not_JSON_answers_RuleNotFound(
        string name)
    {
        CapturingLogger<DryRunRuleQueryHandler> logger = new();

        var result = await DryRunHandler(Seed(RuleNamed("known", 0)), logger).HandleAsync(
            new DryRunRuleQuery(Munich, name, "not json at all"),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<DryRunRuleError.RuleNotFound>();
        refusal.Code.ShouldBe("RULE_NOT_FOUND");
        refusal.Status.ShouldBe(HttpStatusCode.NotFound);
        refusal.Message.ShouldBe($"No rule named '{name}' exists.");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task DryRun_of_a_name_that_is_too_long_to_be_a_RuleName_answers_RuleNotFound_naming_it()
    {
        var name = new string('n', 64);

        var result = await DryRunHandler(Seed(RuleNamed("known", 0)), new CapturingLogger<DryRunRuleQueryHandler>())
            .HandleAsync(new DryRunRuleQuery(Munich, name, null), CancellationToken.None);

        result.Error.ShouldBeOfType<DryRunRuleError.RuleNotFound>().Message.ShouldBe($"No rule named '{name}' exists.");
    }

    [Fact]
    public async Task DryRun_of_an_ambiguous_name_with_a_sample_that_is_not_JSON_answers_FabAmbiguous()
    {
        var source = Seed(
            RuleNamed("shared", 0, fab: "munich"),
            RuleNamed("shared", 1, fab: "dresden"));

        var result = await DryRunHandler(source, new CapturingLogger<DryRunRuleQueryHandler>()).HandleAsync(
            new DryRunRuleQuery(Both, "shared", "not json at all"),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<DryRunRuleError.FabAmbiguous>();
        refusal.Code.ShouldBe("RULE_FAB_AMBIGUOUS");
        refusal.Status.ShouldBe(HttpStatusCode.BadRequest);
        refusal.Fabs.ShouldBe(["dresden", "munich"]);
        refusal.Message.ShouldBe(
            "'shared' exists in more than one of your fabs (dresden, munich). Name the one you mean with ?fabId=.");
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("{\"a\":")]
    public async Task DryRun_of_a_known_rule_with_a_sample_that_is_not_JSON_answers_SampleEventNotJson(string? sample)
    {
        var result = await DryRunHandler(Seed(RuleNamed("known", 0)), new CapturingLogger<DryRunRuleQueryHandler>())
            .HandleAsync(new DryRunRuleQuery(Munich, "known", sample), CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<DryRunRuleError.SampleEventNotJson>();
        refusal.Code.ShouldBe("RULE_DRY_RUN_SAMPLE_INVALID");
        refusal.Status.ShouldBe(HttpStatusCode.BadRequest);
        refusal.Message.ShouldStartWith("The sample event is not valid JSON: ");
        refusal.Message.ShouldBe($"The sample event is not valid JSON: {refusal.Reason}");
    }

    [Fact]
    public async Task DryRun_where_the_predicate_is_false_does_not_evaluate_a_throwing_value_expression()
    {
        var rule = RuleNamed(
            "never-fires",
            0,
            predicate: "$.payload.cycleTime > 100",
            action: RuleAction.SetVariableValue.From("oeeLine1", "1 / 0"));
        CapturingLogger<DryRunRuleQueryHandler> logger = new();

        var result = await DryRunHandler(Seed(rule), logger).HandleAsync(
            new DryRunRuleQuery(Munich, "never-fires", Sample),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new DryRunResultDto(Matched: false, EvaluatedValue: null));
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task DryRun_where_the_predicate_is_not_a_bool_reports_no_match_without_a_log()
    {
        var rule = RuleNamed("numeric-predicate", 0, predicate: "7");
        CapturingLogger<DryRunRuleQueryHandler> logger = new();

        var result = await DryRunHandler(Seed(rule), logger).HandleAsync(
            new DryRunRuleQuery(Munich, "numeric-predicate", Sample),
            CancellationToken.None);

        result.Value.ShouldBe(new DryRunResultDto(Matched: false, EvaluatedValue: null));
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task DryRun_where_the_predicate_is_true_reports_the_value_the_action_would_write()
    {
        var rule = RuleNamed(
            "writes-oee",
            0,
            action: RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2"));
        CapturingLogger<DryRunRuleQueryHandler> logger = new();

        var result = await DryRunHandler(Seed(rule), logger).HandleAsync(
            new DryRunRuleQuery(Munich, "writes-oee", Sample),
            CancellationToken.None);

        result.Value.ShouldBe(new DryRunResultDto(Matched: true, EvaluatedValue: "60"));
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task DryRun_where_the_predicate_is_true_and_the_value_expression_throws_answers_EvaluationFailed_with_its_message_and_logs()
    {
        var rule = RuleNamed(
            "bad-value",
            0,
            action: RuleAction.SetVariableValue.From("oeeLine1", "1 / 0"));
        CapturingLogger<DryRunRuleQueryHandler> logger = new();

        var result = await DryRunHandler(Seed(rule), logger).HandleAsync(
            new DryRunRuleQuery(Munich, "bad-value", Sample),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<DryRunRuleError.EvaluationFailed>();
        refusal.Code.ShouldBe("RULE_DRY_RUN_EVALUATION_FAILED");
        refusal.Status.ShouldBe(HttpStatusCode.BadRequest);
        refusal.Reason.ShouldBe("division by zero");
        refusal.Message.ShouldBe("Evaluating the rule against the sample failed: division by zero");
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldBe($"Dry-run evaluation failed on rule {rule.Id}.");
        logger.Entries[0].Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("division by zero");
    }

    [Fact]
    public async Task DryRun_where_the_predicate_cannot_be_evaluated_against_the_sample_answers_EvaluationFailed_and_logs()
    {
        var rule = RuleNamed("missing-field", 0, predicate: "$.payload.absent <= 30");
        CapturingLogger<DryRunRuleQueryHandler> logger = new();

        var result = await DryRunHandler(Seed(rule), logger).HandleAsync(
            new DryRunRuleQuery(Munich, "missing-field", Sample),
            CancellationToken.None);

        result.Error.ShouldBeOfType<DryRunRuleError.EvaluationFailed>().Reason.ShouldBe(
            "cannot coerce NullValue to a number");
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldBe($"Dry-run evaluation failed on rule {rule.Id}.");
    }

    [Fact]
    public async Task DryRun_of_a_stored_rule_whose_predicate_does_not_parse_reports_the_parser_message_and_logs()
    {
        var rule = RuleNamed("bad-predicate", 0, predicate: "1 +");
        CapturingLogger<DryRunRuleQueryHandler> logger = new();

        var result = await DryRunHandler(Seed(rule), logger).HandleAsync(
            new DryRunRuleQuery(Munich, "bad-predicate", Sample),
            CancellationToken.None);

        result.Error.ShouldBeOfType<DryRunRuleError.EvaluationFailed>().Reason.ShouldBe(
            "AEL parse error at position 3: expected literal, field access, or '('; got ''");
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Message.ShouldBe($"Dry-run evaluation failed on rule {rule.Id}.");
    }

    [Fact]
    public async Task DryRun_whose_arithmetic_overflows_hides_the_exception_text_and_logs_it()
    {
        var rule = RuleNamed("overflow-rule", 0, predicate: "$.payload.big * 10 > 0");
        CapturingLogger<DryRunRuleQueryHandler> logger = new();

        var result = await DryRunHandler(Seed(rule), logger).HandleAsync(
            new DryRunRuleQuery(Munich, "overflow-rule", """{"payload": {"big": 7.9e28}}"""),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<DryRunRuleError.EvaluationFailed>();
        refusal.Reason.ShouldBe("the rule could not be evaluated against this sample");
        refusal.Message.ShouldBe(
            "Evaluating the rule against the sample failed: the rule could not be evaluated against this sample");
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Message.ShouldBe($"Dry-run evaluation failed on rule {rule.Id}.");
        logger.Entries[0].Exception.ShouldBeOfType<OverflowException>();
    }
}
