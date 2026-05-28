using System.Globalization;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Rule.Events;
using SmartSentinelEye.Automation.Domain.Tests.Rule.Fakes;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class RuleStateMachineTests
{
    private static readonly DateTimeOffset Created =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Draft_to_Active_via_Publish_stamps_PublishedAt_and_raises_RulePublishedDomainEvent()
    {
        RuleAggregate rule = new RuleBuilder().WithClock(Created).Build();
        rule.ClearPendingEvents();

        DateTimeOffset publishedMoment = Created.AddHours(1);
        rule.Publish(new FakeClock(publishedMoment));

        rule.State.ShouldBe(RuleState.Active);
        rule.PublishedAt.ShouldBe(publishedMoment);
        rule.PendingEvents.OfType<RulePublishedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Active_to_Archived_via_Archive_stamps_ArchivedAt_and_raises_RuleArchivedDomainEvent()
    {
        RuleAggregate rule = new RuleBuilder().WithClock(Created).Build();
        rule.Publish(new FakeClock(Created.AddHours(1)));
        rule.ClearPendingEvents();

        DateTimeOffset archivedMoment = Created.AddHours(2);
        rule.Archive(new FakeClock(archivedMoment));

        rule.State.ShouldBe(RuleState.Archived);
        rule.ArchivedAt.ShouldBe(archivedMoment);
        rule.PendingEvents.OfType<RuleArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Draft_to_Archived_via_Archive_is_valid_cancellation_path()
    {
        RuleAggregate rule = new RuleBuilder().WithClock(Created).Build();
        rule.ClearPendingEvents();

        rule.Archive(new FakeClock(Created.AddMinutes(5)));

        rule.State.ShouldBe(RuleState.Archived);
        rule.PendingEvents.OfType<RuleArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Publish_is_idempotent_on_an_already_Active_rule()
    {
        RuleAggregate rule = new RuleBuilder().WithClock(Created).Build();
        rule.Publish(new FakeClock(Created.AddHours(1)));
        rule.ClearPendingEvents();

        rule.Publish(new FakeClock(Created.AddHours(2)));

        rule.State.ShouldBe(RuleState.Active);
        rule.PublishedAt.ShouldBe(Created.AddHours(1)); // unchanged
        rule.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Archive_is_idempotent_on_an_already_Archived_rule()
    {
        RuleAggregate rule = new RuleBuilder().WithClock(Created).Build();
        rule.Archive(new FakeClock(Created.AddHours(1)));
        rule.ClearPendingEvents();

        rule.Archive(new FakeClock(Created.AddHours(2)));

        rule.State.ShouldBe(RuleState.Archived);
        rule.ArchivedAt.ShouldBe(Created.AddHours(1)); // unchanged
        rule.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Publish_on_an_Archived_rule_throws()
    {
        RuleAggregate rule = new RuleBuilder().WithClock(Created).Build();
        rule.Archive(new FakeClock(Created.AddHours(1)));

        Action act = () => rule.Publish(new FakeClock(Created.AddHours(2)));
        act.ShouldThrow<InvalidOperationException>();
    }
}
