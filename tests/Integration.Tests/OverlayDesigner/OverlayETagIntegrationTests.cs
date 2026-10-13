using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 012 T021 — the overlay read side hands the caller a version, mirroring
/// LayoutComposition. ADR-0104 keeps the two revisioned contexts in lockstep,
/// so this is the same shape as <c>LayoutETagIntegrationTests</c> on purpose.
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayETagIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reading_an_overlay_returns_an_ETag_matching_the_version_in_the_body()
    {
        using var overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        var overlayIdentifier = await CreateDraftAsync(overlays);

        var fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetched.EnsureSuccessStatusCode();

        var payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        var version = payload.GetProperty("version").GetInt32();

        fetched.Headers.ETag.ShouldNotBeNull();
        fetched.Headers.ETag.Tag.ShouldBe($"\"{version}\"");
        // If-Match rejects weak tags, so a weak ETag here would be unusable.
        fetched.Headers.ETag.IsWeak.ShouldBeFalse();
    }

    [Fact]
    public async Task The_list_endpoint_carries_a_version_on_every_chain()
    {
        using var overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        await CreateDraftAsync(overlays);

        var listed = await overlays.GetAsync("/overlays");
        listed.EnsureSuccessStatusCode();

        var payload = await listed.Content.ReadFromJsonAsync<JsonElement>();
        var chains = payload.GetProperty("chains");

        chains.GetArrayLength().ShouldBeGreaterThan(0);
        foreach (var chain in chains.EnumerateArray())
        {
            chain.TryGetProperty("version", out var version).ShouldBeTrue();
            version.GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public async Task A_mutation_without_If_Match_is_refused_with_428()
    {
        using var overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        var overlayIdentifier = await CreateDraftAsync(overlays);

        var published = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/revisions/1/publish", content: null);

        published.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task A_mutation_carrying_a_superseded_version_is_refused_with_409()
    {
        using var overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        var overlayIdentifier = await CreateDraftAsync(overlays);
        var readAt = await OverlayRequests.VersionAsync(overlays, overlayIdentifier);

        var published = await OverlayRequests.PostAsync(
            overlays, overlayIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        var refused = await overlays.SendAsync(
            OverlayRequests.Conditional(HttpMethod.Post, overlayIdentifier, "revisions/1/archive", readAt));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_REVISION_STALE");
    }

    [Fact]
    public async Task The_same_mutation_succeeds_once_the_caller_re_reads()
    {
        using var overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        var overlayIdentifier = await CreateDraftAsync(overlays);

        var published = await OverlayRequests.PostAsync(
            overlays, overlayIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        var archived = await OverlayRequests.PostAsync(
            overlays, overlayIdentifier, "revisions/1/archive");

        archived.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient overlays)
    {
        var created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Etag-{Guid.NewGuid():N}"[..16],
                elements = new[]
                {
                    new
                    {
                        kind = "Text",
                        color = "#FFFFFFD9",
                        text = "Production Line 1",
                        normalizedX = 0.5m,
                        normalizedY = 0.05m,
                        normalizedWidth = 0.3m,
                        normalizedHeight = 0.08m,
                        fontSizePx = 48,
                    },
                },
            });
        created.EnsureSuccessStatusCode();

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
