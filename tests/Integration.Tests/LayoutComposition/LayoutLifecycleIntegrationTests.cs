using System.Diagnostics;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 003 T024 — end-to-end through the layout-composition API and
/// the underlying Postgres + Wolverine stack. Drives the US1 happy path:
/// create a Draft via <c>POST /layouts</c>, publish revision 1 via
/// <c>POST /layouts/{id}/revisions/1/publish</c>, and assert the
/// transition is observable on <c>GET /layouts/{id}</c> within the
/// 500 ms SLO budget for the synchronous command path.
/// </summary>
[Collection(AspireCollection.Name)]
public class LayoutLifecycleIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_and_publish_a_layout_yields_a_Published_revision_within_500_ms()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");

        Stopwatch sw = Stopwatch.StartNew();
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts",
            new { name = $"Line-{Guid.NewGuid():N}".Substring(0, 16), cameraIdentifier = Guid.CreateVersion7() });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        layoutIdentifier.ShouldNotBe(Guid.Empty);

        HttpResponseMessage published = await layouts.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/publish", content: null);
        sw.Stop();

        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        sw.Elapsed.TotalMilliseconds.ShouldBeLessThan(500,
            $"create + publish took {sw.Elapsed.TotalMilliseconds:F0} ms");

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement revisions = payload.GetProperty("revisions");
        revisions.GetArrayLength().ShouldBe(1);
        revisions[0].GetProperty("state").GetString().ShouldBe("Published");
        revisions[0].GetProperty("revisionNumber").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_name_collision_returns_409_Conflict_with_LAYOUT_NAME_TAKEN()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        string sharedName = $"Cam-{Guid.NewGuid():N}".Substring(0, 16);

        HttpResponseMessage first = await layouts.PostAsJsonAsync(
            "/layouts", new { name = sharedName, cameraIdentifier = Guid.CreateVersion7() });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await layouts.PostAsJsonAsync(
            "/layouts", new { name = sharedName, cameraIdentifier = Guid.CreateVersion7() });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_NAME_TAKEN");
    }

    [Fact]
    public async Task List_with_state_Published_returns_only_chains_with_a_published_revision()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        string draftName = $"Drf-{Guid.NewGuid():N}".Substring(0, 16);
        string pubName = $"Pub-{Guid.NewGuid():N}".Substring(0, 16);

        HttpResponseMessage draftRaw = await layouts.PostAsJsonAsync(
            "/layouts", new { name = draftName, cameraIdentifier = Guid.CreateVersion7() });
        draftRaw.EnsureSuccessStatusCode();

        HttpResponseMessage pubRaw = await layouts.PostAsJsonAsync(
            "/layouts", new { name = pubName, cameraIdentifier = Guid.CreateVersion7() });
        pubRaw.EnsureSuccessStatusCode();
        Guid pubIdentifier = await pubRaw.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage publish = await layouts.PostAsync(
            $"/layouts/{pubIdentifier}/revisions/1/publish", content: null);
        publish.EnsureSuccessStatusCode();

        HttpResponseMessage response = await layouts.GetAsync("/layouts?state=Published");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement published = payload.GetProperty("published");
        IEnumerable<string> names = published.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!);
        names.ShouldContain(pubName);
        names.ShouldNotContain(draftName);
    }

    [Fact]
    public async Task Get_for_an_unknown_layout_returns_404()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        HttpResponseMessage response = await layouts.GetAsync($"/layouts/{Guid.CreateVersion7()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_GET_returns_401()
    {
        HttpResponseMessage response = await aspire.LayoutComposition.GetAsync(
            $"/layouts/{Guid.CreateVersion7()}");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
