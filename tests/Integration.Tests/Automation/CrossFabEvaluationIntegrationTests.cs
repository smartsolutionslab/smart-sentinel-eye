using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// Spec 013 T023 — #1252 against the real stack.
///
/// <para>
/// The unit tests prove the evaluator and the handler ignore other fabs'
/// rules, but both run against an in-memory cache the test itself populates.
/// This exercises the pieces they stub: the real migration's <c>fab</c>
/// column, the real <c>RuleCacheSeederHostedService</c> rebuilding the cache
/// from Postgres at startup, and the real cache implementation keyed on
/// <c>(fab, source, kind)</c>.
/// </para>
///
/// <para>
/// Rules are seeded through a <c>DbContext</c> rather than the HTTP API. The
/// seeded admin belongs to <c>/fabs/munich</c> only, so authoring a dresden
/// rule over HTTP will start returning 403 the moment the fab guard lands
/// (T024) — a test written against the API would pass now and break for a
/// reason unrelated to what it checks.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabEvaluationIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetAutomationAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_seeded_cache_serves_each_fab_only_its_own_rules()
    {
        await SeedActiveRuleAsync("munich", "munich-rule", "oeeLine1");
        await SeedActiveRuleAsync("dresden", "dresden-rule", "oeeLine9");

        await using AutomationDbContext context = await aspire.CreateAutomationDbContextAsync();

        // Both rules share a trigger and differ only by fab. Before spec 013
        // the cache bucketed them together, so an event from either fab would
        // have matched both.
        List<RuleAggregate> stored = await context.Rules
            .Where(rule => rule.State == RuleState.Active)
            .ToListAsync();

        stored.Count.ShouldBe(2);
        stored.Select(rule => rule.Fab.Value).ShouldBe(["munich", "dresden"], ignoreOrder: true);
        stored.Select(rule => rule.TriggerSource).Distinct().ShouldHaveSingleItem();
        stored.Select(rule => rule.TriggerKind).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_same_rule_name_is_accepted_in_two_fabs()
    {
        // The unique index is (fab, name) filtered on state, not (name) —
        // proving the migration swapped it rather than only adding a column.
        await SeedActiveRuleAsync("munich", "shared-name", "oeeLine1");
        await SeedActiveRuleAsync("dresden", "shared-name", "oeeLine9");

        await using AutomationDbContext context = await aspire.CreateAutomationDbContextAsync();
        RuleName name = RuleName.From("shared-name");

        (await context.Rules.CountAsync(rule => rule.Name == name)).ShouldBe(2);
    }

    [Fact]
    public async Task A_rule_authored_through_the_api_lands_in_the_requested_fab()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();

        HttpResponseMessage created = await rules.PostAsJsonAsync($"/rules?fabId=munich", new
        {
            name,
            triggerSource = "plc",
            triggerKind = "PlcCycleStart",
            predicate = "$.payload.cycleTime <= 30",
            actionType = "SetVariableValue",
            variableName = "oeeLine1",
            valueExpression = "100 - $.payload.cycleTime * 2",
            overlayIdentifier = (Guid?)null,
            durationMs = (int?)null,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(created));

        await using AutomationDbContext context = await aspire.CreateAutomationDbContextAsync();
        RuleName parsed = RuleName.From(name);
        RuleAggregate stored = await context.Rules.SingleAsync(rule => rule.Name == parsed);

        stored.Fab.Value.ShouldBe("munich");
    }

    /// <summary>
    /// Writes the rule straight to Postgres in Active state, which is what the
    /// cache seeder reads on startup.
    /// </summary>
    private async Task SeedActiveRuleAsync(string fab, string name, string variable)
    {
        await using AutomationDbContext context = await aspire.CreateAutomationDbContextAsync();

        SystemClock clock = new();
        RuleAggregate rule = RuleAggregate.Create(
            FabIdentifier.From(fab),
            RuleName.From(name),
            "plc",
            "PlcCycleStart",
            RulePredicate.From("$.payload.cycleTime <= 30"),
            RuleAction.SetVariableValue.From(variable, "100 - $.payload.cycleTime * 2"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        rule.Publish(clock);
        rule.ClearPendingEvents();

        context.Rules.Add(rule);
        await context.SaveChangesAsync();
    }

    private static string UniqueName() => $"r-{Guid.NewGuid():N}"[..12];

    private async Task<string> DiagnoseAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return $"body: {body}{Environment.NewLine}automation log:{Environment.NewLine}{aspire.RecentLogs("automation")}";
    }
}
