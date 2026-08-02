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
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        Guid overlayIdentifier = await CreateDraftAsync(overlays);

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetched.EnsureSuccessStatusCode();

        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        int version = payload.GetProperty("version").GetInt32();

        fetched.Headers.ETag.ShouldNotBeNull();
        fetched.Headers.ETag.Tag.ShouldBe($"\"{version}\"");
        // If-Match rejects weak tags, so a weak ETag here would be unusable.
        fetched.Headers.ETag.IsWeak.ShouldBeFalse();
    }

    [Fact]
    public async Task The_list_endpoint_carries_a_version_on_every_chain()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        await CreateDraftAsync(overlays);

        HttpResponseMessage listed = await overlays.GetAsync("/overlays");
        listed.EnsureSuccessStatusCode();

        JsonElement payload = await listed.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement chains = payload.GetProperty("chains");

        chains.GetArrayLength().ShouldBeGreaterThan(0);
        foreach (JsonElement chain in chains.EnumerateArray())
        {
            chain.TryGetProperty("version", out JsonElement version).ShouldBeTrue();
            version.GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public async Task A_mutation_without_If_Match_is_refused_with_428()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        Guid overlayIdentifier = await CreateDraftAsync(overlays);

        HttpResponseMessage published = await overlays.PostAsync(
            $"/overlays/{overlayIdentifier}/revisions/1/publish", content: null);

        published.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task A_mutation_carrying_a_superseded_version_is_refused_with_409()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        Guid overlayIdentifier = await CreateDraftAsync(overlays);
        int readAt = await OverlayRequests.VersionAsync(overlays, overlayIdentifier);

        HttpResponseMessage published = await OverlayRequests.PostAsync(
            overlays, overlayIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        HttpResponseMessage refused = await overlays.SendAsync(
            OverlayRequests.Conditional(HttpMethod.Post, overlayIdentifier, "revisions/1/archive", readAt));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_REVISION_STALE");
    }

    [Fact]
    public async Task The_same_mutation_succeeds_once_the_caller_re_reads()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        Guid overlayIdentifier = await CreateDraftAsync(overlays);

        HttpResponseMessage published = await OverlayRequests.PostAsync(
            overlays, overlayIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        HttpResponseMessage archived = await OverlayRequests.PostAsync(
            overlays, overlayIdentifier, "revisions/1/archive");

        archived.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient overlays)
    {
        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Etag-{Guid.NewGuid():N}"[..16],
                label = new
                {
                    text = "Production Line 1",
                    normalizedX = 0.5m,
                    normalizedY = 0.05m,
                    normalizedWidth = 0.3m,
                    normalizedHeight = 0.08m,
                    fontSizePx = 48,
                },
            });
        created.EnsureSuccessStatusCode();

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
