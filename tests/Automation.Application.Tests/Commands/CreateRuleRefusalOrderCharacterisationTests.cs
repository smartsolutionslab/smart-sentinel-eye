using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Commands;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before the two expression parse checks in
/// <c>CreateRuleCommandHandler.HandleAsync</c> move into a helper. The existing
/// tests exercise each refusal alone and assert only its type; none has two
/// refusable conditions true at once, none reads the position or the message a
/// refusal carries, and none observes whether a refused create reached the
/// repository. Reordering the extracted checks, or letting the value expression
/// be parsed before the predicate, changes which of the two an author is told
/// about first.
/// </summary>
public class CreateRuleRefusalOrderCharacterisationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Author =
        OperatorIdentifier.From(Guid.Parse("00000000-0000-7000-8000-0000000000d1"));

    private static CreateRuleCommand CommandFor(
        string predicate,
        RuleAction action,
        string name = "high-oee-on-fast-cycle",
        string fab = "munich") =>
        new(
            FabIdentifier.From(fab),
            RuleName.From(name),
            TriggerSource.From("plc"),
            TriggerKind.From("PlcCycleStart"),
            RulePredicate.From(predicate),
            action,
            Author);

    private static CreateRuleCommandHandler HandlerFor(
        IRuleRepository rules,
        CapturingLogger<CreateRuleCommandHandler> logger) =>
        new(rules, new FakeClock(Now), logger);

    [Fact]
    public async Task A_bad_predicate_and_a_bad_value_expression_answer_PredicateParseFailed_at_the_predicates_position()
    {
        OrderedRuleRepository rules = new();
        CapturingLogger<CreateRuleCommandHandler> logger = new();

        var result = await HandlerFor(rules, logger).HandleAsync(
            CommandFor("1 +", RuleAction.SetVariableValue.From("oeeLine1", "(2")),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        var refusal = result.Error.ShouldBeOfType<CreateRuleError.PredicateParseFailed>();
        refusal.Position.ShouldBe(3);
        refusal.Reason.ShouldBe("AEL parse error at position 3: expected literal, field access, or '('; got ''");
        refusal.Code.ShouldBe("RULE_PREDICATE_PARSE_FAILED");
        refusal.Status.ShouldBe(HttpStatusCode.BadRequest);
        refusal.Message.ShouldBe(
            "Predicate parse failed at position 3: AEL parse error at position 3: expected literal, field access, or '('; got ''");
        rules.Calls.ShouldBe(["GetByName"]);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_good_predicate_and_a_bad_value_expression_answer_ActionExpressionParseFailed_at_the_expressions_position()
    {
        OrderedRuleRepository rules = new();
        CapturingLogger<CreateRuleCommandHandler> logger = new();

        var result = await HandlerFor(rules, logger).HandleAsync(
            CommandFor("$.payload.cycleTime <= 30", RuleAction.SetVariableValue.From("oeeLine1", "(2")),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        var refusal = result.Error.ShouldBeOfType<CreateRuleError.ActionExpressionParseFailed>();
        refusal.Position.ShouldBe(2);
        refusal.Reason.ShouldBe("AEL parse error at position 2: expected ')'");
        refusal.Code.ShouldBe("RULE_ACTION_EXPRESSION_PARSE_FAILED");
        refusal.Status.ShouldBe(HttpStatusCode.BadRequest);
        refusal.Message.ShouldBe(
            "Action value expression parse failed at position 2: AEL parse error at position 2: expected ')'");
        rules.Calls.ShouldBe(["GetByName"]);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_taken_name_beats_a_bad_predicate_and_a_bad_value_expression()
    {
        OrderedRuleRepository rules = new();
        var firstResult = await HandlerFor(rules, new CapturingLogger<CreateRuleCommandHandler>()).HandleAsync(
            CommandFor("$.payload.cycleTime <= 30", RuleAction.HighlightOverlay.From(Guid.CreateVersion7(), 5_000)),
            CancellationToken.None);
        firstResult.IsSuccess.ShouldBeTrue();
        rules.Calls.Clear();
        CapturingLogger<CreateRuleCommandHandler> logger = new();

        var result = await HandlerFor(rules, logger).HandleAsync(
            CommandFor("1 +", RuleAction.SetVariableValue.From("oeeLine1", "(2")),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<CreateRuleError.RuleNameTaken>();
        refusal.Code.ShouldBe("RULE_NAME_TAKEN");
        refusal.Status.ShouldBe(HttpStatusCode.Conflict);
        refusal.Message.ShouldBe("A non-archived rule named 'high-oee-on-fast-cycle' already exists in fab 'munich'.");
        rules.Calls.ShouldBe(["GetByName"]);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_highlight_action_is_never_parsed_for_a_value_expression_but_its_predicate_still_is()
    {
        OrderedRuleRepository rules = new();

        var result = await HandlerFor(rules, new CapturingLogger<CreateRuleCommandHandler>()).HandleAsync(
            CommandFor("$.payload.cycleTime <=", RuleAction.HighlightOverlay.From(Guid.CreateVersion7(), 5_000)),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<CreateRuleError.PredicateParseFailed>();
        refusal.Position.ShouldBe(22);
        refusal.Reason.ShouldBe("AEL parse error at position 22: expected literal, field access, or '('; got ''");
        rules.Calls.ShouldBe(["GetByName"]);
    }

    [Fact]
    public async Task A_create_that_succeeds_adds_then_saves_then_logs_once_and_answers_the_new_identifier()
    {
        OrderedRuleRepository rules = new();
        CapturingLogger<CreateRuleCommandHandler> logger = new();

        var result = await HandlerFor(rules, logger).HandleAsync(
            CommandFor(
                "$.payload.cycleTime <= 30",
                RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2")),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        rules.Calls.ShouldBe(["GetByName", "Add", "Save"]);
        var created = rules.Stored.ShouldHaveSingleItem();
        created.Id.ShouldBe(result.Value);
        created.State.ShouldBe(RuleState.Draft);
        created.Creation.At.Value.ShouldBe(Now);
        created.Creation.By.ShouldBe(Author);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Information);
        logger.Entries[0].Message.ShouldBe(
            $"Created rule {result.Value} 'high-oee-on-fast-cycle' (plc/PlcCycleStart) by 00000000-0000-7000-8000-0000000000d1.");
        logger.Entries[0].Exception.ShouldBeNull();
    }

    /// <summary>
    /// Wraps the shared in-memory repository and records which members the
    /// handler called, in order: the question here is not only what a refusal
    /// answers but whether it got as far as writing.
    /// </summary>
    private sealed class OrderedRuleRepository : IRuleRepository
    {
        private readonly InMemoryRuleRepository inner = new();

        public List<string> Calls { get; } = [];

        public IReadOnlyList<RuleAggregate> Stored => inner.Rules;

        public Task<Option<RuleAggregate>> GetByIdentifierAsync(
            RuleIdentifier rule,
            CancellationToken cancellationToken)
        {
            Calls.Add("GetByIdentifier");
            return inner.GetByIdentifierAsync(rule, cancellationToken);
        }

        public Task<Option<RuleAggregate>> GetByNameAsync(
            FabIdentifier fab,
            RuleName name,
            CancellationToken cancellationToken)
        {
            Calls.Add("GetByName");
            return inner.GetByNameAsync(fab, name, cancellationToken);
        }

        public void Add(RuleAggregate rule)
        {
            Calls.Add("Add");
            inner.Add(rule);
        }

        public Task SaveAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Save");
            return inner.SaveAsync(cancellationToken);
        }
    }
}
