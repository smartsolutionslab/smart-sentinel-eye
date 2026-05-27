using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 004 T080 — verifies the cross-context binding: an overlay
/// identifier provided when creating a layout draft is persisted on
/// the Layout.Revision and round-trips through both
/// <c>GET /layouts/{id}</c> and <c>GET /overlays/{id}</c>. Exercises
/// the value-copy boundary (ADR-0040) end-to-end.
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayBindingIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private readonly AspireFixture _aspire = aspire;

    public async Task InitializeAsync()
    {
        await _aspire.ResetLayoutCompositionAsync();
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
    public async Task Bound_overlay_appears_in_GET_layout_and_in_GET_overlay()
    {
        using HttpClient overlays = await _aspire.CreateAdminClientAsync("overlay-designer");
        using HttpClient layouts = await _aspire.CreateAdminClientAsync("layout-composition");

        // 1. Create + publish an overlay so it shows up as a binding candidate.
        HttpResponseMessage overlayCreated = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Ovl-{Guid.NewGuid():N}".Substring(0, 16),
                label = SampleLabelBody(),
            });
        overlayCreated.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await overlayCreated.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage overlayPublished = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/revisions/1/publish", content: null);
        overlayPublished.EnsureSuccessStatusCode();

        // 2. Create + publish a layout bound to that overlay.
        HttpResponseMessage layoutCreated = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = $"Bnd-{Guid.NewGuid():N}".Substring(0, 16),
                cameraIdentifier = Guid.CreateVersion7(),
                overlayIdentifier,
            });
        layoutCreated.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid layoutIdentifier = await layoutCreated.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage layoutPublished = await layouts.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/publish", content: null);
        layoutPublished.EnsureSuccessStatusCode();

        // 3. GET /layouts/{id} carries the overlay identifier on the Published revision.
        HttpResponseMessage layoutFetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        layoutFetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement layoutPayload = await layoutFetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement published = layoutPayload.GetProperty("revisions")[0];
        published.GetProperty("overlayIdentifier").GetGuid().ShouldBe(overlayIdentifier);

        // 4. GET /overlays/{id} returns the same overlay with its Published Label.
        HttpResponseMessage overlayFetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        overlayFetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement overlayPayload = await overlayFetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement publishedOverlay = overlayPayload.GetProperty("revisions")[0];
        publishedOverlay.GetProperty("state").GetString().ShouldBe("Published");
        publishedOverlay.GetProperty("text").GetString().ShouldBe("Production Line 1");
    }

    [Fact]
    public async Task An_unbound_layout_carries_overlayIdentifier_null()
    {
        using HttpClient layouts = await _aspire.CreateAdminClientAsync("layout-composition");

        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = $"Sol-{Guid.NewGuid():N}".Substring(0, 16),
                cameraIdentifier = Guid.CreateVersion7(),
            });
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("revisions")[0]
            .GetProperty("overlayIdentifier").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
