using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 240 (#2480) — <c>GridDimensions.From(body.Grid.Rows, body.Grid.Cols)</c>
/// at <c>LayoutEndpoints.Commands.cs:143</c> (<c>POST /layouts</c>) and
/// <c>:354</c> (<c>PATCH /layouts/{id}/revisions/{n}</c>) sits inside
/// <c>catch (ArgumentException ex)</c>. An omitted or explicit-<c>null</c>
/// <c>"grid"</c> leaves <c>body.Grid</c> at CLR <c>null</c>; dereferencing it
/// throws <see cref="NullReferenceException"/>, uncaught, so both endpoints
/// answer 500 where every other malformed body member on the same endpoints
/// answers 400 <c>LAYOUT_INVALID_INPUT</c>.
///
/// <para>
/// The same gap is one member wider than the issue's own wording: a <c>null</c>
/// element of <c>"tiles"</c> escapes the same <c>try</c> in the <c>ParseTiles</c>
/// lambda at <c>:443</c>, the same way. That is spec 240's US2 and is covered
/// here too.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class AbsentLayoutBodyMembersAreRefusedIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetLayoutCompositionAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- FR-001 — POST /layouts, grid absent or null ------------------------

    [Fact]
    public async Task A_layout_create_omitting_the_grid_is_refused_as_invalid_input()
    {
        using HttpClient layouts = await DresdenClientAsync();
        string name = UniqueName();

        HttpResponseMessage refused = await layouts.PostAsJsonAsync(
            "/layouts", new { name, tiles = Array.Empty<object>() });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("grid", Case.Insensitive);

        (await NamesAsync(layouts)).ShouldNotContain(name);
    }

    [Fact]
    public async Task A_layout_create_with_an_explicit_null_grid_is_refused_as_invalid_input()
    {
        using HttpClient layouts = await DresdenClientAsync();
        string name = UniqueName();

        HttpResponseMessage refused = await layouts.PostAsJsonAsync(
            "/layouts", new { name, grid = (object?)null, tiles = Array.Empty<object>() });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("grid", Case.Insensitive);

        (await NamesAsync(layouts)).ShouldNotContain(name);
    }

    // ---- FR-002 — PATCH .../revisions/{n}, grid absent -----------------------

    [Fact]
    public async Task Editing_a_draft_revision_omitting_the_grid_is_refused_as_invalid_input()
    {
        using HttpClient layouts = await DresdenClientAsync();
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire, "dresden");
        Guid layoutIdentifier = await CreateDraftAsync(layouts, camera);

        HttpResponseMessage refused = await LayoutRequests.PatchAsync(
            layouts, layoutIdentifier, "revisions/1", new { tiles = Array.Empty<object>() });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("grid", Case.Insensitive);

        JsonElement layout = await FetchAsync(layouts, layoutIdentifier);
        JsonElement revision1 = RevisionByNumber(layout, 1);
        revision1.GetProperty("gridRows").GetInt32().ShouldBe(1);
        revision1.GetProperty("gridCols").GetInt32().ShouldBe(1);
        revision1.GetProperty("tiles").GetArrayLength().ShouldBe(1);
        revision1.GetProperty("tiles")[0].GetProperty("cameraIdentifier").GetGuid().ShouldBe(camera);
    }

    // ---- FR-003 — a null tile element, on both endpoints ---------------------

    [Fact]
    public async Task A_layout_create_with_a_null_tile_element_is_refused_as_invalid_input()
    {
        using HttpClient layouts = await DresdenClientAsync();
        string name = UniqueName();

        HttpResponseMessage refused = await layouts.PostAsJsonAsync(
            "/layouts",
            new { name, grid = new { rows = 1, cols = 1 }, tiles = new object?[] { null } });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("tile", Case.Insensitive);

        (await NamesAsync(layouts)).ShouldNotContain(name);
    }

    [Fact]
    public async Task Editing_a_draft_revision_with_a_null_tile_element_is_refused_as_invalid_input()
    {
        using HttpClient layouts = await DresdenClientAsync();
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire, "dresden");
        Guid layoutIdentifier = await CreateDraftAsync(layouts, camera);

        HttpResponseMessage refused = await LayoutRequests.PatchAsync(
            layouts, layoutIdentifier, "revisions/1",
            new { grid = new { rows = 1, cols = 1 }, tiles = new object?[] { null } });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("tile", Case.Insensitive);

        JsonElement layout = await FetchAsync(layouts, layoutIdentifier);
        JsonElement revision1 = RevisionByNumber(layout, 1);
        revision1.GetProperty("tiles").GetArrayLength().ShouldBe(1);
        revision1.GetProperty("tiles")[0].GetProperty("cameraIdentifier").GetGuid().ShouldBe(camera);
    }

    // ---- FR-005 green guard — the already-working validation is undisturbed --

    /// <summary>
    /// Pins that closing the null-grid gap does not touch the sibling
    /// zero-rows path: same status, same title, same <c>detail</c> text,
    /// character for character.
    /// </summary>
    [Fact]
    public async Task A_zero_row_grid_keeps_its_existing_refusal_detail()
    {
        using HttpClient layouts = await DresdenClientAsync();
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire, "dresden");
        string name = UniqueName();

        HttpResponseMessage refused = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name,
                grid = new { rows = 0, cols = 2 },
                tiles = new[] { new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 } },
            });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
        problem.GetProperty("detail").GetString().ShouldBe("rows must be >= 1; got 0. (Parameter 'rows')");
    }

    // ---- auth green guard — the null-grid fix does not short-circuit auth ----

    /// <summary>
    /// Pins that closing the null-grid gap does not accidentally move body
    /// validation ahead of authentication: an anonymous caller omitting the
    /// grid still gets 401, not 400.
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_omitting_the_grid_is_challenged_not_validated()
    {
        using HttpClient anonymous = aspire.CreateServiceClient("layout-composition");

        HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            "/layouts", new { name = UniqueName(), tiles = Array.Empty<object>() });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await BodyAsync(response));
    }

    // ---- helpers --------------------------------------------------------------

    private Task<HttpClient> DresdenClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("layout-composition", DresdenOperator, OperatorPassword);

    private static async Task<Guid> CreateDraftAsync(HttpClient layouts, Guid camera)
    {
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 } },
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> FetchAsync(HttpClient layouts, Guid layoutIdentifier)
    {
        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.EnsureSuccessStatusCode();
        return await fetched.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement RevisionByNumber(JsonElement layout, int number) =>
        layout.GetProperty("revisions").EnumerateArray()
            .Single(revision => revision.GetProperty("revisionNumber").GetInt32() == number);

    private static async Task<string[]> NamesAsync(HttpClient layouts)
    {
        HttpResponseMessage listed = await layouts.GetAsync("/layouts");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("chains").EnumerateArray()
            .Select(row => row.GetProperty("name").GetString()!)];
    }

    private static string UniqueName() => $"L-{Guid.NewGuid():N}"[..12];

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"layout-composition log:{Environment.NewLine}{aspire.RecentLogs("layout-composition")}";
}
