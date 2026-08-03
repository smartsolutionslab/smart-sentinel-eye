using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// Spec 013 T023 + T031 — fab scoping against the real stack.
///
/// <para>
/// The unit tests prove the evaluator, the handler and the read handlers
/// ignore other fabs' rules, but all run against in-memory doubles the test
/// itself populates. This exercises what they stub: the real migration's
/// <c>fab</c> column, the real cache seeder reading it from Postgres, the
/// <c>(fab, name)</c> partial unique index, and the endpoints' own fab
/// resolution over real HTTP.
/// </para>
///
/// <para>
/// Rules are seeded through a <c>DbContext</c> rather than the HTTP API,
/// because the seeded admin belongs to <c>/fabs/munich</c> only — authoring a
/// dresden rule over HTTP is now refused, which is the very behaviour under
/// test rather than a way to set it up.
/// </para>
///
/// <para>
/// Not covered here: driving an ingested event through Wolverine and
/// asserting the other fab's variable is untouched. That needs a registered
/// webhook integration, a minted bearer and polling for eventual
/// consistency. Cross-fab *evaluation* is covered by the unit tests in
/// <c>RuleEvaluatorTests</c> and <c>FabEventIngestedV1HandlerTests</c>, each
/// verified against a faithful reproduction of #1252 — so the behaviour is
/// tested, but not end-to-end through the bus.
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

    /// <summary>
    /// What the cache seeder reads: two Active rules that share a trigger and
    /// differ only by fab, round-tripped through the real migration's
    /// <c>fab</c> column.
    ///
    /// <para>
    /// It does not assert on the cache — that is a process-internal singleton
    /// inside the automation service and unreachable from here. The bucketing
    /// itself is covered by <c>InMemoryRuleCacheTests</c> against the shipped
    /// class, each case checked against a reproduction of #1252.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_fabs_rules_survive_the_round_trip_distinguishable_by_fab()
    {
        await SeedActiveRuleAsync("munich", "munich-rule", "oeeLine1");
        await SeedActiveRuleAsync("dresden", "dresden-rule", "oeeLine9");

        await using AutomationDbContext context = await aspire.CreateAutomationDbContextAsync();

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
    /// quickstart.md §3 step 1, as a test rather than a manual step.
    ///
    /// <para>
    /// The seeded admin belongs to exactly one fab, so authoring without
    /// naming one must infer it (ADR-0114). This is the inference path over
    /// real HTTP — the endpoint tests in <c>FabResolutionTests</c> drive the
    /// decision table with synthetic principals, but nothing else proves a
    /// real Keycloak token reaches it with the groups claim intact.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Authoring_without_naming_a_fab_infers_the_operators_own()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();

        // No ?fabId= — deliberately.
        HttpResponseMessage created = await rules.PostAsJsonAsync("/rules", new
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

    /// <summary>Left in Draft, so a state filter has something to exclude.</summary>
    private async Task SeedDraftRuleAsync(string fab, string name)
    {
        await using AutomationDbContext context = await aspire.CreateAutomationDbContextAsync();

        RuleAggregate rule = RuleAggregate.Create(
            FabIdentifier.From(fab),
            RuleName.From(name),
            "plc",
            "PlcCycleStart",
            RulePredicate.From("$.payload.cycleTime <= 30"),
            RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new SystemClock());
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

    // ---- spec 013 T031: another fab's rule is unreachable, and says nothing ----
    //
    // The seeded admin belongs to /fabs/munich only, so a dresden rule is
    // exactly the "not yours" case. Each assertion compares the response to
    // the one for a name that never existed: if they differ in status, code
    // or body, the API confirms the rule exists and an operator can
    // enumerate another fab's names one guess at a time (FR-007).

    [Fact]
    public async Task Reading_another_fabs_rule_is_indistinguishable_from_one_that_never_existed()
    {
        string foreign = UniqueName();
        await SeedActiveRuleAsync("dresden", foreign, "oeeLine9");

        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        HttpResponseMessage notYours = await rules.GetAsync($"/rules/{foreign}");
        HttpResponseMessage neverExisted = await rules.GetAsync($"/rules/{UniqueName()}");

        notYours.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        neverExisted.StatusCode.ShouldBe(notYours.StatusCode);
        (await BodyWithoutDetailAsync(notYours)).ShouldBe(await BodyWithoutDetailAsync(neverExisted));
    }

    [Fact]
    public async Task Publishing_another_fabs_rule_is_refused_and_leaves_it_active()
    {
        string foreign = UniqueName();
        await SeedActiveRuleAsync("dresden", foreign, "oeeLine9");

        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpRequestMessage request = new(HttpMethod.Post, $"/rules/{foreign}/publish");
        request.Headers.TryAddWithoutValidation("If-Match", "\"0\"");

        HttpResponseMessage refused = await rules.SendAsync(request);

        // 404, not 409: the fab check runs before the version comparison, so
        // the concurrency layer never gets to reveal that the rule is there.
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using AutomationDbContext context = await aspire.CreateAutomationDbContextAsync();
        RuleName parsed = RuleName.From(foreign);
        RuleAggregate stored = await context.Rules.SingleAsync(rule => rule.Name == parsed);
        stored.State.ShouldBe(RuleState.Active);
    }

    [Fact]
    public async Task Archiving_another_fabs_rule_is_refused_and_leaves_it_active()
    {
        string foreign = UniqueName();
        await SeedActiveRuleAsync("dresden", foreign, "oeeLine9");

        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpRequestMessage request = new(HttpMethod.Post, $"/rules/{foreign}/archive");
        request.Headers.TryAddWithoutValidation("If-Match", "\"0\"");

        HttpResponseMessage refused = await rules.SendAsync(request);

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using AutomationDbContext context = await aspire.CreateAutomationDbContextAsync();
        RuleName parsed = RuleName.From(foreign);
        RuleAggregate stored = await context.Rules.SingleAsync(rule => rule.Name == parsed);
        stored.State.ShouldBe(RuleState.Active);
    }

    [Fact]
    public async Task Dry_running_another_fabs_rule_is_refused()
    {
        // A trial run persists nothing, which is exactly why it would make a
        // convenient side channel for learning how another fab's rule behaves.
        string foreign = UniqueName();
        await SeedActiveRuleAsync("dresden", foreign, "oeeLine9");

        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        HttpResponseMessage refused = await rules.PostAsJsonAsync(
            $"/rules/{foreign}/dry-run", new { sampleEvent = "{\"payload\":{\"cycleTime\":27}}" });

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The only coverage of fab-scoped listing over HTTP, and the first test in
    /// the repo to call <c>GET /rules</c> at all — which is how the endpoint
    /// shipped broken and stayed that way (#1298).
    /// </summary>
    [Fact]
    public async Task The_listing_omits_another_fabs_rules()
    {
        string foreign = UniqueName();
        string own = UniqueName();
        await SeedActiveRuleAsync("dresden", foreign, "oeeLine9");
        await SeedActiveRuleAsync("munich", own, "oeeLine1");

        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        HttpResponseMessage listed = await rules.GetAsync("/rules");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(listed));

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();
        string[] names = [.. rows.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];

        names.ShouldContain(own);
        names.ShouldNotContain(foreign);
    }

    /// <summary>
    /// The filters are optional, and #1298 was precisely the endpoint not
    /// agreeing: they were declared non-nullable, so omitting them was a
    /// binding failure rather than "no filter". Supplying one has to keep
    /// working, or the fix would have traded one broken call for another.
    /// </summary>
    [Fact]
    public async Task The_listing_still_filters_when_a_filter_is_supplied()
    {
        string draft = UniqueName();
        string active = UniqueName();
        await SeedDraftRuleAsync("munich", draft);
        await SeedActiveRuleAsync("munich", active, "oeeLine1");

        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        HttpResponseMessage listed = await rules.GetAsync("/rules?state=Active");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(listed));

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();
        string[] names = [.. rows.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];

        names.ShouldContain(active);
        names.ShouldNotContain(draft);
    }

    /// <summary>
    /// The problem body with the per-request fields removed. <c>detail</c>
    /// echoes the requested name and <c>traceId</c> is unique per request, so
    /// both differ between the two calls for reasons that carry no
    /// information about the rule. Everything else — status, title, type —
    /// must match, or the response distinguishes "not yours" from "never
    /// existed".
    /// </summary>
    private static async Task<string> BodyWithoutDetailAsync(HttpResponseMessage response)
    {
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        return string.Join(
            "|",
            problem.EnumerateObject()
                .Where(property => !string.Equals(property.Name, "detail", StringComparison.Ordinal))
                .Where(property => !string.Equals(property.Name, "traceId", StringComparison.Ordinal))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.Value}"));
    }
}
