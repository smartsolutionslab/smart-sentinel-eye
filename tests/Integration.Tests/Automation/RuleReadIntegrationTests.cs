using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// Regression cover for #1241 — the rule read handlers filtered on
/// <c>candidate.Name.Value</c>, reaching inside a value object that EF maps
/// with a whole-property conversion. That cannot be translated, so every call
/// threw before the query reached the database and returned 500.
///
/// <para>
/// **These have to be integration tests.** The unit suite passes against the
/// broken code because <c>InMemoryRuleQuerySource</c> is LINQ-to-Objects,
/// where <c>Name.Value</c> evaluates perfectly well. A fake more permissive
/// than the real provider is what hid a total outage of this context's read
/// surface for as long as it has existed.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RuleReadIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetAutomationAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_rule_can_be_read_back_by_name()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();
        (await CreateAsync(rules, name)).EnsureSuccessStatusCode();

        HttpResponseMessage fetched = await rules.GetAsync($"/rules/{name}");

        fetched.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(fetched));
        (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("name").GetString().ShouldBe(name);
    }

    [Fact]
    public async Task An_unknown_rule_reads_as_404_not_500()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        HttpResponseMessage fetched = await rules.GetAsync($"/rules/{UniqueName()}");

        fetched.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(fetched));
    }

    // A name that is not a legal RuleName cannot match a stored row, so it is
    // not-found rather than an unhandled parse failure.
    [Fact]
    public async Task A_malformed_rule_name_reads_as_404_not_500()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        HttpResponseMessage fetched = await rules.GetAsync("/rules/NOT_A_VALID_NAME");

        fetched.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(fetched));
    }

    [Fact]
    public async Task Dry_run_resolves_the_rule_instead_of_500ing()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string name = UniqueName();
        (await CreateAsync(rules, name)).EnsureSuccessStatusCode();

        HttpResponseMessage dryRun = await rules.PostAsJsonAsync(
            $"/rules/{name}/dry-run",
            new
            {
                sampleEvent = """{"source":"plc","kind":"PlcCycleStart","device":"press-1","payload":{"cycleTime":20}}""",
            });

        dryRun.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(dryRun));
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

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

    private static string UniqueName() => $"r-{Guid.NewGuid():N}"[..12];
}
