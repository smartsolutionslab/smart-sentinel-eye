using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 017 T021 — SC-001, SC-002 and SC-007 over real HTTP with real
/// Keycloak tokens.
///
/// <para>
/// The handler tests prove the queries and the lookup filter on the caller's
/// fabs, but they pass the fabs in themselves. This exercises the leg they
/// stub: that a real token's groups claim survives to the endpoint and
/// becomes that filter — and that the full ADR-0114 write table behaves as
/// specified rather than as assumed.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class LayoutFabScopingIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetLayoutCompositionAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- the write decision table (ADR-0114) --------------------------------

    /// <summary>
    /// Dresden, not munich. Everything else in the system defaults to munich,
    /// so an inference that fell back to the default would pass against a
    /// munich operator and only fail here.
    /// </summary>
    [Fact]
    public async Task A_single_fab_operator_has_dresden_inferred_not_the_default()
    {
        using HttpClient layouts = await LayoutsFor(DresdenOperator);
        string name = UniqueName();

        HttpResponseMessage created = await layouts.PostAsJsonAsync("/layouts", Body(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        (await FabOfAsync(layouts, await created.Content.ReadFromJsonAsync<Guid>()))
            .ShouldBe("dresden");
    }

    [Fact]
    public async Task A_multi_fab_operator_naming_no_fab_is_refused()
    {
        // Not inferred and not tie-broken: either would file the layout under a
        // fab the operator never chose (ADR-0114).
        using HttpClient layouts = await LayoutsFor(MultiFabOperator);

        HttpResponseMessage refused = await layouts.PostAsJsonAsync("/layouts", Body(UniqueName()));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_FAB_REQUIRED");
    }

    [Fact]
    public async Task A_multi_fab_operator_naming_one_of_their_fabs_is_accepted()
    {
        using HttpClient layouts = await LayoutsFor(MultiFabOperator);

        HttpResponseMessage created = await layouts.PostAsJsonAsync("/layouts?fabId=dresden", Body(UniqueName()));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        (await FabOfAsync(layouts, await created.Content.ReadFromJsonAsync<Guid>())).ShouldBe("dresden");
    }

    /// <summary>
    /// 403, not 404 — the caller named a *fab*, so the answer is about the fab
    /// and hides nothing. Contrast with addressing a layout, below.
    /// </summary>
    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_is_refused()
    {
        using HttpClient layouts = await LayoutsFor(DresdenOperator);

        HttpResponseMessage refused = await layouts.PostAsJsonAsync("/layouts?fabId=munich", Body(UniqueName()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
    }

    /// <summary>SC-007 — FR-019, and the half that is a leak.</summary>
    [Fact]
    public async Task The_same_name_is_usable_in_two_fabs()
    {
        using HttpClient multi = await LayoutsFor(MultiFabOperator);
        string name = UniqueName();

        (await multi.PostAsJsonAsync("/layouts?fabId=munich", Body(name)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await multi.PostAsJsonAsync("/layouts?fabId=dresden", Body(name));

        second.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(second));
    }

    [Fact]
    public async Task The_same_name_is_still_refused_within_one_fab()
    {
        using HttpClient layouts = await LayoutsFor(DresdenOperator);
        string name = UniqueName();

        (await layouts.PostAsJsonAsync("/layouts", Body(name))).StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await layouts.PostAsJsonAsync("/layouts", Body(name));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(second));
    }

    // ---- the reads (SC-001, SC-002) ----------------------------------------

    [Fact]
    public async Task A_dresden_operator_sees_only_dresden_layouts()
    {
        (string inMunich, string inDresden) = await OnePerFabAsync();

        using HttpClient layouts = await LayoutsFor(DresdenOperator);
        string[] names = await NamesAsync(layouts);

        names.ShouldContain(inDresden);
        names.ShouldNotContain(inMunich);
    }

    [Fact]
    public async Task A_multi_fab_operator_sees_both_plants()
    {
        (string inMunich, string inDresden) = await OnePerFabAsync();

        using HttpClient layouts = await LayoutsFor(MultiFabOperator);
        string[] names = await NamesAsync(layouts);

        names.ShouldContain(inMunich);
        names.ShouldContain(inDresden);
    }

    /// <summary>
    /// SC-002 — compared field by field, not by status alone. A 404 whose body
    /// said "not yours" would leak precisely what FR-006 withholds, and both
    /// responses are 404 either way.
    /// </summary>
    [Fact]
    public async Task Another_fabs_layout_is_indistinguishable_from_one_that_never_existed()
    {
        Guid inMunich = await CreateAsync(MultiFabOperator, "?fabId=munich", UniqueName());
        Guid neverExisted = Guid.CreateVersion7();

        using HttpClient layouts = await LayoutsFor(DresdenOperator);

        HttpResponseMessage hidden = await layouts.GetAsync($"/layouts/{inMunich}");
        HttpResponseMessage absent = await layouts.GetAsync($"/layouts/{neverExisted}");

        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(hidden));
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(absent));

        (await NormalisedProblemAsync(hidden, inMunich))
            .ShouldBe(await NormalisedProblemAsync(absent, neverExisted));
    }

    /// <summary>
    /// FR-006 on a write, and 404 rather than 403: the caller addressed a
    /// layout, so "forbidden" would confirm it exists.
    /// </summary>
    [Fact]
    public async Task Publishing_another_fabs_layout_is_reported_as_not_found()
    {
        Guid inMunich = await CreateAsync(MultiFabOperator, "?fabId=munich", UniqueName());

        using HttpClient layouts = await LayoutsFor(DresdenOperator);
        HttpRequestMessage publish = new(HttpMethod.Post, $"/layouts/{inMunich}/revisions/1/publish");
        publish.Headers.TryAddWithoutValidation("If-Match", "\"0\"");

        HttpResponseMessage refused = await layouts.SendAsync(publish);

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(refused));
    }

    // ---- helpers ------------------------------------------------------------

    private async Task<(string InMunich, string InDresden)> OnePerFabAsync()
    {
        string inMunich = UniqueName();
        string inDresden = UniqueName();
        await CreateAsync(MultiFabOperator, "?fabId=munich", inMunich);
        await CreateAsync(MultiFabOperator, "?fabId=dresden", inDresden);
        return (inMunich, inDresden);
    }

    private async Task<Guid> CreateAsync(string username, string fabQuery, string name)
    {
        using HttpClient layouts = await LayoutsFor(username);
        HttpResponseMessage created = await layouts.PostAsJsonAsync($"/layouts{fabQuery}", Body(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private Task<HttpClient> LayoutsFor(string username) =>
        aspire.CreateAuthenticatedClientAsync("layout-composition", username, OperatorPassword);

    private static object Body(string name) => new
    {
        name,
        grid = new { rows = 1, cols = 1 },
        tiles = new[]
        {
            new { cameraIdentifier = Guid.CreateVersion7(), overlayIdentifier = (Guid?)null, row = 0, col = 0 },
        },
    };

    private static async Task<string[]> NamesAsync(HttpClient layouts)
    {
        HttpResponseMessage listed = await layouts.GetAsync("/layouts");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("chains").EnumerateArray()
            .Select(row => row.GetProperty("name").GetString()!)];
    }

    private static async Task<string> FabOfAsync(HttpClient layouts, Guid layout)
    {
        HttpResponseMessage read = await layouts.GetAsync($"/layouts/{layout}");
        read.EnsureSuccessStatusCode();
        JsonElement payload = await read.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("fab").GetString()!;
    }

    /// <summary>
    /// Two things are normalised out and only two: <c>traceId</c>, which
    /// differs per request by design, and the layout identifier the caller
    /// itself supplied, which the two requests cannot share. What is left is
    /// the whole of the response, and it must match byte for byte.
    /// </summary>
    private static async Task<string> NormalisedProblemAsync(HttpResponseMessage response, Guid requested)
    {
        JsonNode problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem.AsObject().Remove("traceId");

        return problem.ToJsonString()
            .Replace(requested.ToString(), "<requested-layout>", StringComparison.OrdinalIgnoreCase);
    }

    private static string UniqueName() => $"L-{Guid.NewGuid():N}"[..12];

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"layout-composition log:{Environment.NewLine}{aspire.RecentLogs("layout-composition")}";
}
