using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Tests.Commands;

public class CreateRuleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static CreateRuleCommand HappyCommand(
        string name = "high-oee-on-fast-cycle",
        string predicate = "$.payload.cycleTime <= 30",
        RuleAction? action = null,
        string fab = "munich") =>
        new(
            FabIdentifier.From(fab),
            RuleName.From(name),
            "plc",
            "PlcCycleStart",
            RulePredicate.From(predicate),
            action ?? RuleAction.SetVariableValue.From(
                "oeeLine1", "100 - $.payload.cycleTime * 2"),
            OperatorIdentifier.From(Guid.CreateVersion7()));

    [Fact]
    public async Task First_create_with_a_unique_name_returns_a_new_RuleIdentifier()
    {
        InMemoryRuleRepository repo = new();
        CreateRuleCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<CreateRuleCommandHandler>.Instance);

        Result<RuleIdentifier, CreateRuleError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.Rules.ShouldHaveSingleItem().State.ShouldBe(RuleState.Draft);
    }

    [Fact]
    public async Task Name_collision_returns_RuleNameTaken()
    {
        InMemoryRuleRepository repo = new();
        CreateRuleCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<CreateRuleCommandHandler>.Instance);

        Result<RuleIdentifier, CreateRuleError> first =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);
        first.IsSuccess.ShouldBeTrue();

        Result<RuleIdentifier, CreateRuleError> second =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        second.IsSuccess.ShouldBeFalse();
        second.Error.ShouldBeOfType<CreateRuleError.RuleNameTaken>();
    }

    [Fact]
    public async Task Malformed_predicate_returns_PredicateParseFailed_with_position()
    {
        InMemoryRuleRepository repo = new();
        CreateRuleCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<CreateRuleCommandHandler>.Instance);

        Result<RuleIdentifier, CreateRuleError> result = await handler.HandleAsync(
            HappyCommand(predicate: "$.payload.cycleTime <="),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<CreateRuleError.PredicateParseFailed>();
        repo.Rules.ShouldBeEmpty();
    }

    [Fact]
    public async Task Malformed_value_expression_returns_ActionExpressionParseFailed()
    {
        InMemoryRuleRepository repo = new();
        CreateRuleCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<CreateRuleCommandHandler>.Instance);

        Result<RuleIdentifier, CreateRuleError> result = await handler.HandleAsync(
            HappyCommand(action: RuleAction.SetVariableValue.From("oeeLine1", "1 +")),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<CreateRuleError.ActionExpressionParseFailed>();
    }

    [Fact]
    public async Task HighlightOverlay_action_skips_value_expression_parsing()
    {
        InMemoryRuleRepository repo = new();
        CreateRuleCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<CreateRuleCommandHandler>.Instance);

        Result<RuleIdentifier, CreateRuleError> result = await handler.HandleAsync(
            HappyCommand(action: RuleAction.HighlightOverlay.From(Guid.CreateVersion7(), 5_000)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    // ---- spec 013 US4: names are unique per fab, not globally ----

    [Fact]
    public async Task The_same_name_is_accepted_in_a_second_fab()
    {
        InMemoryRuleRepository repo = new();
        CreateRuleCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<CreateRuleCommandHandler>.Instance);

        Result<RuleIdentifier, CreateRuleError> first =
            await handler.HandleAsync(HappyCommand(fab: "munich"), CancellationToken.None);
        Result<RuleIdentifier, CreateRuleError> second =
            await handler.HandleAsync(HappyCommand(fab: "dresden"), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldNotBe(first.Value);
        repo.Rules.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_same_name_twice_in_one_fab_is_still_refused()
    {
        InMemoryRuleRepository repo = new();
        CreateRuleCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<CreateRuleCommandHandler>.Instance);

        await handler.HandleAsync(HappyCommand(fab: "munich"), CancellationToken.None);
        Result<RuleIdentifier, CreateRuleError> duplicate =
            await handler.HandleAsync(HappyCommand(fab: "munich"), CancellationToken.None);

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Code.ShouldBe("RULE_NAME_TAKEN");
        repo.Rules.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_conflict_names_the_fab_the_name_is_taken_in()
    {
        // Without the fab, an operator who can see only their own is told a
        // name is unavailable with no way to find out why.
        InMemoryRuleRepository repo = new();
        CreateRuleCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<CreateRuleCommandHandler>.Instance);

        await handler.HandleAsync(HappyCommand(fab: "dresden"), CancellationToken.None);
        Result<RuleIdentifier, CreateRuleError> duplicate =
            await handler.HandleAsync(HappyCommand(fab: "dresden"), CancellationToken.None);

        duplicate.Error.Message.ShouldContain("dresden");
    }
}
