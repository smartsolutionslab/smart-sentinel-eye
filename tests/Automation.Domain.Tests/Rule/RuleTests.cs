using System.Globalization;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Rule.Events;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class RuleTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Create_starts_the_rule_in_Draft_with_the_given_envelope()
    {
        RuleAggregate rule = new RuleBuilder().WithClock(Now).Build();

        rule.State.ShouldBe(RuleState.Draft);
        rule.Name.Value.ShouldBe("high-oee-on-fast-cycle");
        rule.TriggerSource.ShouldBe("plc");
        rule.TriggerKind.ShouldBe("PlcCycleStart");
        rule.CreatedAt.ShouldBe(Now);
        rule.PublishedAt.ShouldBeNull();
        rule.ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public void Create_raises_a_RuleCreatedDomainEvent_carrying_the_envelope()
    {
        RuleAggregate rule = new RuleBuilder().WithClock(Now).Build();

        RuleCreatedDomainEvent raised = rule.PendingEvents
            .OfType<RuleCreatedDomainEvent>()
            .ShouldHaveSingleItem();
        raised.Rule.ShouldBe(rule.Id);
        raised.Name.ShouldBe(rule.Name);
        raised.TriggerSource.ShouldBe("plc");
        raised.TriggerKind.ShouldBe("PlcCycleStart");
        raised.CreatedAt.ShouldBe(Now);
    }
}
