using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Commands;

public class ArchiveRuleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static RuleAggregate Seed(InMemoryRuleRepository repo)
    {
        RuleAggregate rule = RuleAggregate.Create(
            RuleName.From("high-oee-on-fast-cycle"),
            "plc",
            "PlcCycleStart",
            RulePredicate.From("$.payload.cycleTime <= 30"),
            RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Add(rule);
        return rule;
    }

    [Fact]
    public async Task Archives_an_Active_rule_and_evicts_it_from_the_cache()
    {
        InMemoryRuleRepository repo = new();
        InMemoryRuleCache cache = new();
        RuleAggregate seeded = Seed(repo);
        seeded.Publish(new FakeClock(Now.AddHours(1)));
        cache.Upsert(seeded);

        ArchiveRuleCommandHandler handler = new(
            repo, cache, new FakeClock(Now.AddHours(2)),
            NullLogger<ArchiveRuleCommandHandler>.Instance);

        Result<RuleIdentifier, ArchiveRuleError> result = await handler.HandleAsync(
            new ArchiveRuleCommand(seeded.Name), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seeded.State.ShouldBe(RuleState.Archived);
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_rule_returns_RuleNotFound()
    {
        InMemoryRuleRepository repo = new();
        InMemoryRuleCache cache = new();

        ArchiveRuleCommandHandler handler = new(
            repo, cache, new FakeClock(Now),
            NullLogger<ArchiveRuleCommandHandler>.Instance);

        Result<RuleIdentifier, ArchiveRuleError> result = await handler.HandleAsync(
            new ArchiveRuleCommand(RuleName.From("ghost")), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<ArchiveRuleError.RuleNotFound>();
    }
}
