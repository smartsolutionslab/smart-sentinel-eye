using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 012 T015 — the read side has to hand the caller a version, or there
/// is nothing to put in <c>If-Match</c> and the cross-request check
/// degrades to no check at all (ADR-0113 Layer 1).
/// </summary>
[Collection(AspireCollection.Name)]
public class LayoutETagIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reading_a_layout_returns_an_ETag_matching_the_version_in_the_body()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid layoutIdentifier = await CreateDraftAsync(layouts);

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.EnsureSuccessStatusCode();

        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        int version = payload.GetProperty("version").GetInt32();

        fetched.Headers.ETag.ShouldNotBeNull();
        fetched.Headers.ETag.Tag.ShouldBe($"\"{version}\"");
        fetched.Headers.ETag.IsWeak.ShouldBeFalse();
    }

    [Fact]
    public async Task The_list_endpoint_carries_a_version_on_every_chain()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        await CreateDraftAsync(layouts);

        HttpResponseMessage listed = await layouts.GetAsync("/layouts");
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
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid layoutIdentifier = await CreateDraftAsync(layouts);

        HttpResponseMessage published = await layouts.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/publish", content: null);

        published.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    // The scenario ADR-0043 describes and an EF token can never see: a caller
    // acting on a version it read before someone else moved the chain.
    [Fact]
    public async Task A_mutation_carrying_a_superseded_version_is_refused_with_409()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid layoutIdentifier = await CreateDraftAsync(layouts);
        int readAt = await LayoutRequests.VersionAsync(layouts, layoutIdentifier);

        HttpResponseMessage published = await LayoutRequests.PostAsync(
            layouts, layoutIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        HttpRequestMessage stale = new(HttpMethod.Post, $"/layouts/{layoutIdentifier}/revisions/1/archive");
        stale.Headers.TryAddWithoutValidation("If-Match", $"\"{readAt}\"");
        HttpResponseMessage refused = await layouts.SendAsync(stale);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_REVISION_STALE");
    }

    [Fact]
    public async Task The_same_mutation_succeeds_once_the_caller_re_reads()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid layoutIdentifier = await CreateDraftAsync(layouts);

        HttpResponseMessage published = await LayoutRequests.PostAsync(
            layouts, layoutIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        HttpResponseMessage archived = await LayoutRequests.PostAsync(
            layouts, layoutIdentifier, "revisions/1/archive");

        archived.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient layouts)
    {
        string name = $"Etag-{Guid.NewGuid():N}"[..16];

        HttpResponseMessage created = await layouts.PostAsJsonAsync("/layouts", new
        {
            name,
            grid = new { rows = 1, cols = 1 },
            tiles = new[]
            {
                new { cameraIdentifier = Guid.CreateVersion7(), overlayIdentifier = (Guid?)null, row = 0, col = 0 },
            },
        });
        created.EnsureSuccessStatusCode();

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
