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
    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object SampleLabelBody() => new
    {
        kind = "Text",
        color = "#9F9F9FD9",
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
        using var overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");

        // 1. Create + publish an overlay so it shows up as a binding candidate.
        var overlayCreated = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Ovl-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody() },
            });
        overlayCreated.EnsureSuccessStatusCode();
        var overlayIdentifier = await overlayCreated.Content.ReadFromJsonAsync<Guid>();
        var overlayPublished = await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/1/publish");
        overlayPublished.EnsureSuccessStatusCode();

        // 2. Create + publish a layout bound to that overlay.
        var layoutCreated = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = $"Bnd-{Guid.NewGuid():N}".Substring(0, 16),
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { new { cameraIdentifier = await LayoutRequests.RegisterCameraAsync(aspire), overlayIdentifier = (Guid?)overlayIdentifier, row = 0, col = 0 } },
            });
        layoutCreated.StatusCode.ShouldBe(HttpStatusCode.Created);
        var layoutIdentifier = await layoutCreated.Content.ReadFromJsonAsync<Guid>();
        var layoutPublished = await LayoutRequests.PostAsync(
            layouts, layoutIdentifier, "revisions/1/publish");
        layoutPublished.EnsureSuccessStatusCode();

        // 3. GET /layouts/{id} carries the overlay identifier on the Published revision.
        var layoutFetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        layoutFetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var layoutPayload = await layoutFetched.Content.ReadFromJsonAsync<JsonElement>();
        var published = layoutPayload.GetProperty("revisions")[0];
        published.GetProperty("tiles")[0].GetProperty("overlayIdentifier").GetGuid().ShouldBe(overlayIdentifier);

        // 4. GET /overlays/{id} returns the same overlay with its Published Label.
        var overlayFetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        overlayFetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var overlayPayload = await overlayFetched.Content.ReadFromJsonAsync<JsonElement>();
        var publishedOverlay = overlayPayload.GetProperty("revisions")[0];
        publishedOverlay.GetProperty("state").GetString().ShouldBe("Published");
        publishedOverlay.GetProperty("elements")[0].GetProperty("text").GetString().ShouldBe("Production Line 1");
    }

    [Fact]
    public async Task An_unbound_layout_carries_overlayIdentifier_null()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");

        var created = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = $"Sol-{Guid.NewGuid():N}".Substring(0, 16),
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { new { cameraIdentifier = await LayoutRequests.RegisterCameraAsync(aspire), overlayIdentifier = (Guid?)null, row = 0, col = 0 } },
            });
        created.EnsureSuccessStatusCode();
        var layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        var fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        var payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("revisions")[0]
            .GetProperty("tiles")[0].GetProperty("overlayIdentifier").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
