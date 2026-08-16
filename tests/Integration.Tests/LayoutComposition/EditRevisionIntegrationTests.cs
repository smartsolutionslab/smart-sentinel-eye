using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 003 T089 — drives the US4 revision-chain path end-to-end:
/// publish revision 1, branch + edit a new draft, publish the new
/// revision, observe that revision 1 was auto-archived in the same
/// transaction.
/// </summary>
[Collection(AspireCollection.Name)]
public class EditRevisionIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publishing_a_new_revision_atomically_archives_the_previous_Published_revision()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid camera1 = await LayoutRequests.RegisterCameraAsync(aspire);
        Guid camera2 = await LayoutRequests.RegisterCameraAsync(aspire);

        Guid layoutIdentifier = await CreateAndPublishAsync(layouts, "Line-Edit", camera1);

        HttpResponseMessage branched = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "draft");
        branched.StatusCode.ShouldBe(HttpStatusCode.Created);
        int draftNumber = await branched.Content.ReadFromJsonAsync<int>();
        draftNumber.ShouldBe(2);

        HttpResponseMessage edited = await LayoutRequests.PatchAsync(
            layouts, layoutIdentifier, $"revisions/{draftNumber}",
            new
            {
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { new { cameraIdentifier = camera2, overlayIdentifier = (Guid?)null, row = 0, col = 0 } },
            });
        edited.EnsureSuccessStatusCode();

        HttpResponseMessage published = await LayoutRequests.PostAsync(
            layouts, layoutIdentifier, $"revisions/{draftNumber}/publish");
        published.EnsureSuccessStatusCode();

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.EnsureSuccessStatusCode();
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement revisions = payload.GetProperty("revisions");
        revisions.GetArrayLength().ShouldBe(2);

        Dictionary<int, JsonElement> byNumber = revisions
            .EnumerateArray()
            .ToDictionary(e => e.GetProperty("revisionNumber").GetInt32(), e => e);
        byNumber[1].GetProperty("state").GetString().ShouldBe("Archived");
        byNumber[2].GetProperty("state").GetString().ShouldBe("Published");
        byNumber[2].GetProperty("tiles").EnumerateArray().Single()
            .GetProperty("cameraIdentifier").GetGuid().ShouldBe(camera2);
    }

    [Fact]
    public async Task Reverting_a_Published_revision_brings_it_back_to_Draft()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid layoutIdentifier = await CreateAndPublishAsync(
            layouts, "Line-Revert", await LayoutRequests.RegisterCameraAsync(aspire));

        HttpResponseMessage reverted = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/revert");
        reverted.EnsureSuccessStatusCode();

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("revisions")[0].GetProperty("state").GetString().ShouldBe("Draft");
    }

    [Fact]
    public async Task Branching_a_chain_without_a_Published_revision_returns_409()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", SingleTileBody($"Drf-{Guid.NewGuid():N}".Substring(0, 16), await LayoutRequests.RegisterCameraAsync(aspire)));
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage branchAttempt = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "draft");
        branchAttempt.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<Guid> CreateAndPublishAsync(HttpClient layouts, string namePrefix, Guid camera)
    {
        string name = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 16);
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", SingleTileBody(name, camera));
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage published = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();
        return layoutIdentifier;
    }

    private static object SingleTileBody(string name, Guid camera) => new
    {
        name,
        grid = new { rows = 1, cols = 1 },
        tiles = new[] { new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 } },
    };
}
