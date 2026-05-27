using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 004 T094 — drives the US4 branch/edit/publish flow through the
/// API: publish v1, branch a draft v2, edit v2's label, publish v2,
/// and assert v1 is atomically Archived while v2 is Published
/// (FR-003 atomic-swap on a multi-revision overlay chain).
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayRevisionLifecycleIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private readonly AspireFixture _aspire = aspire;

    public async Task InitializeAsync()
    {
        await _aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object SampleLabelBody(string text = "Production Line 1", int fontSizePx = 48) => new
    {
        text,
        normalizedX = 0.5m,
        normalizedY = 0.05m,
        normalizedWidth = 0.3m,
        normalizedHeight = 0.08m,
        fontSizePx,
    };

    [Fact]
    public async Task Publish_a_new_revision_atomically_archives_the_previous_published_revision()
    {
        using HttpClient overlays = await _aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Rev-{Guid.NewGuid():N}".Substring(0, 16),
                label = SampleLabelBody(),
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        // v1 → Published.
        HttpResponseMessage publishOne = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/revisions/1/publish", content: null);
        publishOne.EnsureSuccessStatusCode();

        // Branch v2 (Draft, label inherited from v1).
        HttpResponseMessage branched = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/draft", content: null);
        branched.StatusCode.ShouldBe(HttpStatusCode.Created);
        int v2 = await branched.Content.ReadFromJsonAsync<int>();
        v2.ShouldBe(2);

        // Edit v2's label.
        HttpResponseMessage edited = await overlays.PatchAsJsonAsync(
            $"/overlays/{overlayIdentifier}/revisions/{v2}",
            new { label = SampleLabelBody("Updated label", 64) });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Publish v2 → atomic swap: v1 becomes Archived, v2 becomes Published.
        HttpResponseMessage publishTwo = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/revisions/{v2}/publish", content: null);
        publishTwo.EnsureSuccessStatusCode();

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement revisions = payload.GetProperty("revisions");
        revisions.GetArrayLength().ShouldBe(2);

        JsonElement r1 = revisions.EnumerateArray().Single(r => r.GetProperty("revisionNumber").GetInt32() == 1);
        JsonElement r2 = revisions.EnumerateArray().Single(r => r.GetProperty("revisionNumber").GetInt32() == 2);
        r1.GetProperty("state").GetString().ShouldBe("Archived");
        r2.GetProperty("state").GetString().ShouldBe("Published");
        r2.GetProperty("text").GetString().ShouldBe("Updated label");
        r2.GetProperty("fontSizePx").GetInt32().ShouldBe(64);
    }

    [Fact]
    public async Task Revert_brings_a_Published_revision_back_to_Draft()
    {
        using HttpClient overlays = await _aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Rvt-{Guid.NewGuid():N}".Substring(0, 16),
                label = SampleLabelBody(),
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage publish = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/revisions/1/publish", content: null);
        publish.EnsureSuccessStatusCode();

        HttpResponseMessage revert = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/revisions/1/revert", content: null);
        revert.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement revision = payload.GetProperty("revisions")[0];
        revision.GetProperty("state").GetString().ShouldBe("Draft");
        revision.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
