using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 014 T030 — the fab decision table for the variables API, over real
/// HTTP. Covers SC-002 and SC-007.
///
/// <para>
/// <c>FabResolutionTests</c> drives the same table with synthetic principals,
/// but nothing else proves a real Keycloak token reaches these endpoints with
/// the groups claim intact. Mirrors
/// <c>RuleFabResolutionIntegrationTests</c>, which exists for the same reason
/// on the rules API.
/// </para>
///
/// <para>
/// Inference is asserted as <b>dresden</b> deliberately. Everything else in
/// the suite defaults to munich, so a broken inference that fell back to the
/// default would pass against a munich operator and only fail here.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class VariableFabResolutionIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetSystemVariablesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_multi_fab_operator_defining_without_naming_a_fab_is_refused()
    {
        // Not inferred, and not tie-broken: either would place the variable in
        // a fab the operator never chose (ADR-0114).
        using HttpClient variables = await ClientFor(MultiFabOperator);

        HttpResponseMessage refused = await variables.PostAsJsonAsync("/system-variables", Body(UniqueName()));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("VARIABLE_FAB_REQUIRED");
    }

    [Fact]
    public async Task A_multi_fab_operator_naming_one_of_their_fabs_is_accepted()
    {
        using HttpClient variables = await ClientFor(MultiFabOperator);
        string name = UniqueName();

        HttpResponseMessage created = await variables.PostAsJsonAsync(
            "/system-variables?fabId=dresden", Body(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        HttpResponseMessage read = await variables.GetAsync($"/system-variables/{name}?fabId=dresden");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(read));
        JsonElement variable = await read.Content.ReadFromJsonAsync<JsonElement>();
        variable.GetProperty("fab").GetString().ShouldBe("dresden");
    }

    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_is_refused()
    {
        // The dresden-only operator against munich: 403, not 404. Nothing is
        // hidden here — the caller named a fab, and the answer is about the
        // fab, not about whether a variable exists in it.
        using HttpClient variables = await ClientFor(DresdenOperator);

        HttpResponseMessage refused = await variables.PostAsJsonAsync(
            "/system-variables?fabId=munich", Body(UniqueName()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
    }

    [Fact]
    public async Task A_single_fab_operator_has_dresden_inferred_not_the_default()
    {
        using HttpClient variables = await ClientFor(DresdenOperator);
        string name = UniqueName();

        HttpResponseMessage created = await variables.PostAsJsonAsync("/system-variables", Body(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        HttpResponseMessage read = await variables.GetAsync($"/system-variables/{name}");
        JsonElement variable = await read.Content.ReadFromJsonAsync<JsonElement>();
        variable.GetProperty("fab").GetString().ShouldBe("dresden");
    }

    [Fact]
    public async Task A_name_held_in_two_of_the_callers_fabs_is_refused_as_ambiguous()
    {
        using HttpClient variables = await ClientFor(MultiFabOperator);
        string shared = UniqueName();

        (await variables.PostAsJsonAsync("/system-variables?fabId=munich", Body(shared)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await variables.PostAsJsonAsync("/system-variables?fabId=dresden", Body(shared)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage ambiguous = await variables.GetAsync($"/system-variables/{shared}");

        ambiguous.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(ambiguous));
        JsonElement problem = await ambiguous.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("VARIABLE_FAB_AMBIGUOUS");
        // Names the candidates, so the caller can retry without guessing.
        string detail = problem.GetProperty("detail").GetString()!;
        detail.ShouldContain("dresden");
        detail.ShouldContain("munich");
    }

    [Fact]
    public async Task Naming_the_fab_resolves_the_ambiguity()
    {
        using HttpClient variables = await ClientFor(MultiFabOperator);
        string shared = UniqueName();

        await variables.PostAsJsonAsync("/system-variables?fabId=munich", Body(shared));
        await variables.PostAsJsonAsync("/system-variables?fabId=dresden", Body(shared));

        HttpResponseMessage resolved = await variables.GetAsync($"/system-variables/{shared}?fabId=munich");

        resolved.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(resolved));
        JsonElement variable = await resolved.Content.ReadFromJsonAsync<JsonElement>();
        variable.GetProperty("fab").GetString().ShouldBe("munich");
    }

    /// <summary>
    /// FR-009 over real HTTP: the dresden-only operator asking for a munich
    /// variable gets the same answer as for a name nobody ever used. Compared
    /// field by field, because a difference in status *or* code is enough to
    /// let an operator enumerate another fab's names one guess at a time.
    /// </summary>
    [Fact]
    public async Task Another_fabs_variable_is_indistinguishable_from_one_that_never_existed()
    {
        using HttpClient owner = await ClientFor(MultiFabOperator);
        string foreign = UniqueName();
        (await owner.PostAsJsonAsync("/system-variables?fabId=munich", Body(foreign)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using HttpClient outsider = await ClientFor(DresdenOperator);

        HttpResponseMessage notYours = await outsider.GetAsync($"/system-variables/{foreign}");
        HttpResponseMessage neverExisted = await outsider.GetAsync($"/system-variables/{UniqueName()}");

        notYours.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(notYours));
        neverExisted.StatusCode.ShouldBe(notYours.StatusCode);
        (await ComparableBodyAsync(notYours)).ShouldBe(await ComparableBodyAsync(neverExisted));
    }

    [Fact]
    public async Task The_listing_spans_every_fab_the_caller_holds()
    {
        // FR-008, and the asymmetry with the write path: a read does not have
        // to choose.
        using HttpClient variables = await ClientFor(MultiFabOperator);
        string inMunich = UniqueName();
        string inDresden = UniqueName();

        await variables.PostAsJsonAsync("/system-variables?fabId=munich", Body(inMunich));
        await variables.PostAsJsonAsync("/system-variables?fabId=dresden", Body(inDresden));

        HttpResponseMessage listed = await variables.GetAsync("/system-variables");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));

        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        string[] names = [.. page.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];

        names.ShouldContain(inMunich);
        names.ShouldContain(inDresden);
    }

    [Fact]
    public async Task The_listing_omits_a_fab_the_caller_does_not_hold()
    {
        using HttpClient owner = await ClientFor(MultiFabOperator);
        string inMunich = UniqueName();
        (await owner.PostAsJsonAsync("/system-variables?fabId=munich", Body(inMunich)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using HttpClient outsider = await ClientFor(DresdenOperator);
        HttpResponseMessage listed = await outsider.GetAsync("/system-variables");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));

        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        string[] names = [.. page.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];

        names.ShouldNotContain(inMunich);
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("system-variables", username, OperatorPassword);

    private static object Body(string name) => new
    {
        name,
        type = "Number",
        initialValue = "1",
        truthyLabel = (string?)null,
        falsyLabel = (string?)null,
    };

    private static string UniqueName() => $"v{Guid.NewGuid():N}"[..12];

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"system-variables log:{Environment.NewLine}{aspire.RecentLogs("system-variables")}";

    /// <summary>
    /// The problem body with the per-request fields removed. <c>detail</c>
    /// echoes the requested name and <c>traceId</c> is unique per request, so
    /// both differ for reasons that carry no information about the variable.
    /// Everything else — status, title, type — must match.
    /// </summary>
    private static async Task<string> ComparableBodyAsync(HttpResponseMessage response)
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
