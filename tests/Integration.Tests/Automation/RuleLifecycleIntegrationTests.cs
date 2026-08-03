using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// First integration coverage for Automation, and the concurrency behaviour on
/// top of it (spec 012 T035). Like SystemVariables before #1230, this context
/// had no suite at all — nothing exercised its endpoints end-to-end.
///
/// <para>
/// The lifecycle tests are the baseline the concurrency tests need to be
/// attributable: without them, a failure here could be the new `If-Match`
/// requirement or ground nobody had ever checked.
///
/// <para>
/// The unknown-name and dry-run cases live in
/// <c>RuleReadIntegrationTests</c> instead — they are regression cover for
/// #1241, which this suite is what exposed.
/// </para>
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RuleLifecycleIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetAutomationAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_created_rule_is_readable_by_name_and_starts_as_Draft()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();

        HttpResponseMessage created = await CreateAsync(rules, name);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(created));

        JsonElement payload = await ReadAsync(rules, name);
        payload.GetProperty("name").GetString().ShouldBe(name);
        payload.GetProperty("state").GetString().ShouldBe("Draft");
        payload.GetProperty("version").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Publishing_moves_the_rule_from_Draft_to_Active()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();
        (await CreateAsync(rules, name)).EnsureSuccessStatusCode();

        (await RuleRequests.PostAsync(rules, name, "publish")).EnsureSuccessStatusCode();

        (await ReadAsync(rules, name)).GetProperty("state").GetString().ShouldBe("Active");
    }

    [Fact]
    public async Task Reading_a_rule_returns_an_ETag_matching_the_version_in_the_body()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();
        (await CreateAsync(rules, name)).EnsureSuccessStatusCode();

        HttpResponseMessage fetched = await rules.GetAsync($"/rules/{name}");
        fetched.EnsureSuccessStatusCode();
        int version = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32();

        fetched.Headers.ETag.ShouldNotBeNull();
        fetched.Headers.ETag.Tag.ShouldBe($"\"{version}\"");
        fetched.Headers.ETag.IsWeak.ShouldBeFalse();
    }


    [Fact]
    public async Task A_mutation_without_If_Match_is_refused_with_428()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();
        (await CreateAsync(rules, name)).EnsureSuccessStatusCode();

        HttpResponseMessage refused = await rules.PostAsync($"/rules/{name}/publish?fabId=munich", content: null);

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task A_mutation_carrying_a_superseded_version_is_refused_with_409()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();
        (await CreateAsync(rules, name)).EnsureSuccessStatusCode();
        int readAt = await RuleRequests.VersionAsync(rules, name);

        (await RuleRequests.PostAsync(rules, name, "publish")).EnsureSuccessStatusCode();

        HttpResponseMessage refused = await rules.SendAsync(
            RuleRequests.Conditional(name, "archive", readAt));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RULE_STALE");

        // The first writer's publish survives — a status-only assertion would
        // pass even if both writes had landed. A published rule is Active, not
        // "Published": RuleState is Draft | Active | Archived.
        (await ReadAsync(rules, name)).GetProperty("state").GetString().ShouldBe("Active");
    }


    /// <summary>
    /// A bare "500" tells a reader nothing, and CI has no other route to the
    /// service's stack trace. Attach the response body and the automation
    /// service's recent output to the assertion message so an unexpected status
    /// is diagnosable from the CI log alone.
    /// </summary>
    private async Task<string> DiagnoseAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return $"body: {body}{Environment.NewLine}automation log:{Environment.NewLine}{aspire.RecentLogs("automation")}";
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient rules, string name) =>
        rules.PostAsJsonAsync("/rules?fabId=munich", new
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

    private static async Task<JsonElement> ReadAsync(HttpClient rules, string name)
    {
        HttpResponseMessage fetched = await rules.GetAsync($"/rules/{name}");
        fetched.EnsureSuccessStatusCode();

        return await fetched.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Rule names are unique per context and the fixture is shared, so each test
    // mints its own rather than relying on reset ordering — a collision would
    // fail in a way that looks like a concurrency bug.
    private static string UniqueName() => $"r-{Guid.NewGuid():N}"[..12];
}
