using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Commands;

public class PublishRuleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static RuleAggregate Seed(InMemoryRuleRepository repo, string name = "high-oee-on-fast-cycle")
    {
        RuleAggregate rule = new RuleBuilder().WithName(name).WithClock(Now).Build();
        repo.Add(rule);
        return rule;
    }

    [Fact]
    public async Task Publishes_a_Draft_rule_and_upserts_into_the_cache()
    {
        InMemoryRuleRepository repo = new();
        InMemoryRuleCache cache = new();
        RuleAggregate seeded = Seed(repo);

        PublishRuleCommandHandler handler = new(
            repo, cache, new FakeClock(Now.AddHours(1)),
            NullLogger<PublishRuleCommandHandler>.Instance);

        Result<RuleIdentifier, PublishRuleError> result = await handler.HandleAsync(
            new PublishRuleCommand(seeded.Name), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seeded.State.ShouldBe(RuleState.Active);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_rule_returns_RuleNotFound()
    {
        InMemoryRuleRepository repo = new();
        InMemoryRuleCache cache = new();

        PublishRuleCommandHandler handler = new(
            repo, cache, new FakeClock(Now),
            NullLogger<PublishRuleCommandHandler>.Instance);

        Result<RuleIdentifier, PublishRuleError> result = await handler.HandleAsync(
            new PublishRuleCommand(RuleName.From("ghost")), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<PublishRuleError.RuleNotFound>();
    }

    [Fact]
    public async Task Publish_is_idempotent_on_an_already_Active_rule()
    {
        InMemoryRuleRepository repo = new();
        InMemoryRuleCache cache = new();
        RuleAggregate seeded = Seed(repo);

        PublishRuleCommandHandler handler = new(
            repo, cache, new FakeClock(Now.AddHours(1)),
            NullLogger<PublishRuleCommandHandler>.Instance);

        await handler.HandleAsync(new PublishRuleCommand(seeded.Name), CancellationToken.None);
        Result<RuleIdentifier, PublishRuleError> second = await handler.HandleAsync(
            new PublishRuleCommand(seeded.Name), CancellationToken.None);

        second.IsSuccess.ShouldBeTrue();
        seeded.State.ShouldBe(RuleState.Active);
    }
}
