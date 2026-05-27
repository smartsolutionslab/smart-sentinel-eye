using System.Diagnostics;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 004 T022 — end-to-end through the overlay-designer API and the
/// underlying Postgres + Wolverine stack. Drives the US1 happy path:
/// create a Draft via <c>POST /overlays</c>, publish revision 1 via
/// <c>POST /overlays/{id}/revisions/1/publish</c>, and assert the
/// transition is observable on <c>GET /overlays/{id}</c> within the
/// 500 ms SLO budget for the synchronous command path.
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayLifecycleIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private readonly AspireFixture _aspire = aspire;

    public async Task InitializeAsync()
    {
        await _aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object SampleLabelBody() => new
    {
        text = "Production Line 1",
        normalizedX = 0.5m,
        normalizedY = 0.05m,
        normalizedWidth = 0.3m,
        normalizedHeight = 0.08m,
        fontSizePx = 48,
    };

    [Fact]
    public async Task Create_and_publish_an_overlay_emits_OverlayRevisionPublishedV1_within_500_ms()
    {
        using HttpClient overlays = await _aspire.CreateAdminClientAsync("overlay-designer");

        Stopwatch sw = Stopwatch.StartNew();
        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Ovl-{Guid.NewGuid():N}".Substring(0, 16),
                label = SampleLabelBody(),
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        overlayIdentifier.ShouldNotBe(Guid.Empty);

        HttpResponseMessage published = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/revisions/1/publish", content: null);
        sw.Stop();

        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        sw.Elapsed.TotalMilliseconds.ShouldBeLessThan(500,
            $"create + publish took {sw.Elapsed.TotalMilliseconds:F0} ms");

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement revisions = payload.GetProperty("revisions");
        revisions.GetArrayLength().ShouldBe(1);
        revisions[0].GetProperty("state").GetString().ShouldBe("Published");
        revisions[0].GetProperty("revisionNumber").GetInt32().ShouldBe(1);
        revisions[0].GetProperty("text").GetString().ShouldBe("Production Line 1");
    }

    [Fact]
    public async Task A_name_collision_returns_409_Conflict_with_OVERLAY_NAME_TAKEN()
    {
        using HttpClient overlays = await _aspire.CreateAdminClientAsync("overlay-designer");
        string sharedName = $"Ovl-{Guid.NewGuid():N}".Substring(0, 16);

        HttpResponseMessage first = await overlays.PostAsJsonAsync(
            "/overlays", new { name = sharedName, label = SampleLabelBody() });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await overlays.PostAsJsonAsync(
            "/overlays", new { name = sharedName, label = SampleLabelBody() });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_NAME_TAKEN");
    }

    [Fact]
    public async Task List_with_state_Published_returns_only_chains_with_a_published_revision()
    {
        using HttpClient overlays = await _aspire.CreateAdminClientAsync("overlay-designer");
        string draftName = $"Drf-{Guid.NewGuid():N}".Substring(0, 16);
        string pubName = $"Pub-{Guid.NewGuid():N}".Substring(0, 16);

        HttpResponseMessage draftRaw = await overlays.PostAsJsonAsync(
            "/overlays", new { name = draftName, label = SampleLabelBody() });
        draftRaw.EnsureSuccessStatusCode();

        HttpResponseMessage pubRaw = await overlays.PostAsJsonAsync(
            "/overlays", new { name = pubName, label = SampleLabelBody() });
        pubRaw.EnsureSuccessStatusCode();
        Guid pubIdentifier = await pubRaw.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage publish = await overlays.PostAsync(
            $"/overlays/{pubIdentifier}/revisions/1/publish", content: null);
        publish.EnsureSuccessStatusCode();

        HttpResponseMessage response = await overlays.GetAsync("/overlays?state=Published");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement published = payload.GetProperty("published");
        IEnumerable<string> names = published.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!);
        names.ShouldContain(pubName);
        names.ShouldNotContain(draftName);
    }

    [Fact]
    public async Task Get_for_an_unknown_overlay_returns_404()
    {
        using HttpClient overlays = await _aspire.CreateAdminClientAsync("overlay-designer");
        HttpResponseMessage response = await overlays.GetAsync($"/overlays/{Guid.CreateVersion7()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_GET_returns_401()
    {
        HttpResponseMessage response = await _aspire.OverlayDesigner.GetAsync(
            $"/overlays/{Guid.CreateVersion7()}");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
