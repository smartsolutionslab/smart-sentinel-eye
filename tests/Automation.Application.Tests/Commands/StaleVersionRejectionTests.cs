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

/// <summary>
/// ADR-0113 Layer 1 for Automation. Each rejection test also asserts the rule
/// was left alone — the check is only worth having if it runs *before* the
/// mutation, and a handler that rejected afterwards would return the right
/// error while corrupting state.
/// </summary>
public class StaleVersionRejectionTests
{
    private const int Stale = 41;

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publish_rejects_a_stale_version_and_leaves_the_rule_in_Draft()
    {
        (InMemoryRuleRepository rules, RuleAggregate rule) = Seeded();
        RuleState before = rule.State;

        Result<RuleIdentifier, PublishRuleError> result = await Publisher(rules).HandleAsync(
            new PublishRuleCommand(FabIdentifier.From("munich"), rule.Name, Stale), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RULE_STALE");
        rule.State.ShouldBe(before);
    }

    [Fact]
    public async Task Archive_rejects_a_stale_version_and_leaves_the_state_alone()
    {
        (InMemoryRuleRepository rules, RuleAggregate rule) = Seeded();
        RuleState before = rule.State;

        Result<RuleIdentifier, ArchiveRuleError> result = await Archiver(rules).HandleAsync(
            new ArchiveRuleCommand(FabIdentifier.From("munich"), rule.Name, Stale), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RULE_STALE");
        rule.State.ShouldBe(before);
    }

    [Fact]
    public async Task The_matching_version_is_accepted()
    {
        (InMemoryRuleRepository rules, RuleAggregate rule) = Seeded();

        Result<RuleIdentifier, PublishRuleError> result = await Publisher(rules).HandleAsync(
            new PublishRuleCommand(FabIdentifier.From("munich"), rule.Name, rule.Version), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    private static PublishRuleCommandHandler Publisher(InMemoryRuleRepository rules) =>
        new(rules, new InMemoryRuleCache(), new FakeClock(Moment), NullLogger<PublishRuleCommandHandler>.Instance);

    private static ArchiveRuleCommandHandler Archiver(InMemoryRuleRepository rules) =>
        new(rules, new InMemoryRuleCache(), new FakeClock(Moment), NullLogger<ArchiveRuleCommandHandler>.Instance);

    private static (InMemoryRuleRepository, RuleAggregate) Seeded()
    {
        InMemoryRuleRepository rules = new();
        RuleAggregate rule = new RuleBuilder().WithName("high-oee").WithClock(Moment).Build();
        rules.Add(rule);

        return (rules, rule);
    }
}
