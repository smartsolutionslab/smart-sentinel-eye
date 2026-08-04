using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// Spec 013 T036 — the fab-resolution decision table (ADR-0114) over real
/// HTTP, for a caller who holds more than one fab.
///
/// <para>
/// <c>FabResolutionTests</c> drives all four rows against synthetic
/// principals, and <c>CrossFabEvaluationIntegrationTests</c> covers the
/// single-fab inference path. What neither could reach was the multi-fab
/// branch: until the realm gained <c>/fabs/dresden</c> and
/// <c>op-multi@smart-sentinel-eye.test</c>, no principal existed that could
/// take it. It was the branch most likely to regress unnoticed, because
/// nothing could exercise it.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RuleFabResolutionIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetAutomationAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_multi_fab_operator_authoring_without_naming_a_fab_is_refused()
    {
        // Not inferred, and not tie-broken: either would place the rule in a
        // fab the operator never chose (ADR-0114).
        using HttpClient rules = await ClientFor(MultiFabOperator);

        HttpResponseMessage refused = await rules.PostAsJsonAsync("/rules", RuleBody(UniqueName()));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RULE_FAB_REQUIRED");
    }

    [Fact]
    public async Task A_multi_fab_operator_naming_one_of_their_fabs_is_accepted()
    {
        using HttpClient rules = await ClientFor(MultiFabOperator);
        string name = UniqueName();

        HttpResponseMessage created = await rules.PostAsJsonAsync($"/rules?fabId=dresden", RuleBody(name));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        HttpResponseMessage read = await rules.GetAsync($"/rules/{name}?fabId=dresden");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(read));
        JsonElement rule = await read.Content.ReadFromJsonAsync<JsonElement>();
        rule.GetProperty("fab").GetString().ShouldBe("dresden");
    }

    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_is_refused()
    {
        // The dresden-only operator against munich: 403, not 404. Nothing is
        // being hidden here — the caller named a fab, and the answer is about
        // the fab, not about whether a rule exists in it.
        using HttpClient rules = await ClientFor(DresdenOperator);

        HttpResponseMessage refused = await rules.PostAsJsonAsync("/rules?fabId=munich", RuleBody(UniqueName()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
    }

    [Fact]
    public async Task A_single_fab_operator_still_has_their_fab_inferred()
    {
        // The row that already worked, re-asserted from a second fab: it must
        // infer dresden, not the munich that everything else defaults to.
        using HttpClient rules = await ClientFor(DresdenOperator);
        string name = UniqueName();

        HttpResponseMessage created = await rules.PostAsJsonAsync("/rules", RuleBody(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        HttpResponseMessage read = await rules.GetAsync($"/rules/{name}");
        JsonElement rule = await read.Content.ReadFromJsonAsync<JsonElement>();
        rule.GetProperty("fab").GetString().ShouldBe("dresden");
    }

    /// <summary>
    /// The 400 introduced with the per-fab unique index: a name is unique
    /// within a fab, so a caller holding two fabs that each contain it has
    /// asked a question with two answers. Before this account existed the
    /// path could only be reached in a unit test.
    /// </summary>
    [Fact]
    public async Task A_name_held_in_two_of_the_callers_fabs_is_refused_as_ambiguous()
    {
        using HttpClient rules = await ClientFor(MultiFabOperator);
        string shared = UniqueName();

        (await rules.PostAsJsonAsync($"/rules?fabId=munich", RuleBody(shared)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await rules.PostAsJsonAsync($"/rules?fabId=dresden", RuleBody(shared)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage ambiguous = await rules.GetAsync($"/rules/{shared}");

        ambiguous.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(ambiguous));
        JsonElement problem = await ambiguous.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RULE_FAB_AMBIGUOUS");
        // Names the candidates, so the caller can retry without guessing.
        string detail = problem.GetProperty("detail").GetString()!;
        detail.ShouldContain("dresden");
        detail.ShouldContain("munich");
    }

    [Fact]
    public async Task Naming_the_fab_resolves_the_ambiguity()
    {
        using HttpClient rules = await ClientFor(MultiFabOperator);
        string shared = UniqueName();

        await rules.PostAsJsonAsync($"/rules?fabId=munich", RuleBody(shared));
        await rules.PostAsJsonAsync($"/rules?fabId=dresden", RuleBody(shared));

        HttpResponseMessage resolved = await rules.GetAsync($"/rules/{shared}?fabId=munich");

        resolved.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(resolved));
        JsonElement rule = await resolved.Content.ReadFromJsonAsync<JsonElement>();
        rule.GetProperty("fab").GetString().ShouldBe("munich");
    }

    [Fact]
    public async Task The_listing_spans_every_fab_the_caller_holds()
    {
        using HttpClient rules = await ClientFor(MultiFabOperator);
        string inMunich = UniqueName();
        string inDresden = UniqueName();

        await rules.PostAsJsonAsync($"/rules?fabId=munich", RuleBody(inMunich));
        await rules.PostAsJsonAsync($"/rules?fabId=dresden", RuleBody(inDresden));

        HttpResponseMessage listed = await rules.GetAsync("/rules");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));

        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        string[] names = [.. page.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];

        names.ShouldContain(inMunich);
        names.ShouldContain(inDresden);
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("automation", username, OperatorPassword);

    private static object RuleBody(string name) => new
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
    };

    private static string UniqueName() => $"r-{Guid.NewGuid():N}"[..12];

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"automation log:{Environment.NewLine}{aspire.RecentLogs("automation")}";
}
