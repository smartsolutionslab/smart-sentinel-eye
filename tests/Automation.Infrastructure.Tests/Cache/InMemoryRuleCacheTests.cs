using System.Globalization;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule;
using SmartSentinelEye.Automation.Infrastructure.Cache;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Infrastructure.Tests.Cache;

/// <summary>
/// The shipped rule cache, tested directly.
///
/// <para>
/// Every other assertion about fab-scoped lookup runs against a hand-written
/// double in the Application test project that reimplements this class line
/// for line. Two implementations kept in step by hand is exactly how the fab
/// key silently reverts in one of them — and #1252, the defect the key exists
/// to close, is invisible to the evaluator tests when the double is the one
/// being exercised. This suite is the only thing that fails if the shipped
/// cache is wrong.
/// </para>
/// </summary>
public class InMemoryRuleCacheTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static RuleAggregate ActiveRule(
        string fab, string name, string source = "plc", string kind = "PlcCycleStart", int minutesLate = 0)
    {
        RuleBuilder builder = new RuleBuilder()
            .WithFab(fab)
            .WithName(name)
            .WithTriggerSource(source)
            .WithTriggerKind(kind)
            .WithClock(Moment.AddMinutes(minutesLate));

        RuleAggregate rule = builder.Build();
        rule.Publish(builder.Clock);

        return rule;
    }

    [Fact]
    public void An_event_matches_only_rules_from_its_own_fab()
    {
        // #1252 in one assertion: before the fab joined the key, both rules
        // shared a bucket and a munich event fired dresden's rule too.
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule("munich", "munich-rule"));
        cache.Upsert(ActiveRule("dresden", "dresden-rule"));

        IReadOnlyList<CompiledRule> munich =
            cache.LookupActive(FabIdentifier.From("munich"), "plc", "PlcCycleStart");

        munich.Count.ShouldBe(1);
        munich[0].Fab.Value.ShouldBe("munich");
    }

    [Fact]
    public void A_fab_with_no_rules_matches_nothing_even_when_the_trigger_is_in_use()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule("munich", "munich-rule"));

        cache.LookupActive(FabIdentifier.From("dresden"), "plc", "PlcCycleStart").ShouldBeEmpty();
    }

    [Fact]
    public void The_same_name_in_two_fabs_occupies_two_buckets()
    {
        // Names are unique per fab, not globally, so this collision is legal
        // and must not let one rule displace the other.
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule("munich", "shared"));
        cache.Upsert(ActiveRule("dresden", "shared"));

        cache.Count.ShouldBe(2);
        cache.LookupActive(FabIdentifier.From("munich"), "plc", "PlcCycleStart").Count.ShouldBe(1);
        cache.LookupActive(FabIdentifier.From("dresden"), "plc", "PlcCycleStart").Count.ShouldBe(1);
    }

    [Fact]
    public void A_bucket_is_ordered_by_CreatedAt_so_the_last_write_wins()
    {
        // FR-012: the evaluator emits effects in this order, so a bucket that
        // came back unsorted would silently invert which value survives.
        InMemoryRuleCache cache = new();
        RuleAggregate later = ActiveRule("munich", "later", minutesLate: 10);
        RuleAggregate earlier = ActiveRule("munich", "earlier");

        cache.Upsert(later);
        cache.Upsert(earlier);

        IReadOnlyList<CompiledRule> bucket =
            cache.LookupActive(FabIdentifier.From("munich"), "plc", "PlcCycleStart");

        bucket.Select(rule => rule.Identifier).ShouldBe([earlier.Id, later.Id]);
    }

    [Fact]
    public void Re_upserting_a_rule_replaces_it_rather_than_duplicating_it()
    {
        InMemoryRuleCache cache = new();
        RuleAggregate rule = ActiveRule("munich", "repeated");

        cache.Upsert(rule);
        cache.Upsert(rule);

        cache.Count.ShouldBe(1);
    }

    [Fact]
    public void A_rule_that_is_not_Active_never_enters_the_cache()
    {
        // The seeder and the publish handler both push through Upsert; a Draft
        // that slipped in would fire before anyone published it.
        InMemoryRuleCache cache = new();

        cache.Upsert(new RuleBuilder().WithFab("munich").WithName("draft-rule").Build());

        cache.Count.ShouldBe(0);
        cache.LookupActive(FabIdentifier.From("munich"), "plc", "PlcCycleStart").ShouldBeEmpty();
    }

    [Fact]
    public void Removing_a_rule_leaves_the_other_fabs_bucket_alone()
    {
        InMemoryRuleCache cache = new();
        RuleAggregate munich = ActiveRule("munich", "munich-rule");
        cache.Upsert(munich);
        cache.Upsert(ActiveRule("dresden", "dresden-rule"));

        cache.Remove(munich.Id);

        cache.LookupActive(FabIdentifier.From("munich"), "plc", "PlcCycleStart").ShouldBeEmpty();
        cache.LookupActive(FabIdentifier.From("dresden"), "plc", "PlcCycleStart").Count.ShouldBe(1);
    }

    [Fact]
    public void A_different_trigger_in_the_same_fab_is_a_different_bucket()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule("munich", "cycle-rule", kind: "PlcCycleStart"));
        cache.Upsert(ActiveRule("munich", "stop-rule", kind: "PlcCycleStop"));

        cache.LookupActive(FabIdentifier.From("munich"), "plc", "PlcCycleStart").Count.ShouldBe(1);
        cache.LookupActive(FabIdentifier.From("munich"), "plc", "PlcCycleStop").Count.ShouldBe(1);
    }

    [Fact]
    public void The_returned_bucket_is_a_snapshot_a_later_write_cannot_mutate()
    {
        // Lookup sits on the evaluation path and the cache is a process-wide
        // singleton; handing out the live list would let a concurrent publish
        // mutate a bucket mid-evaluation.
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule("munich", "first"));

        IReadOnlyList<CompiledRule> snapshot =
            cache.LookupActive(FabIdentifier.From("munich"), "plc", "PlcCycleStart");
        cache.Upsert(ActiveRule("munich", "second", minutesLate: 5));

        snapshot.Count.ShouldBe(1);
    }
}
