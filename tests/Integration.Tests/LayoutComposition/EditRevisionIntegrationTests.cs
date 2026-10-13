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
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var camera1 = await LayoutRequests.RegisterCameraAsync(aspire);
        var camera2 = await LayoutRequests.RegisterCameraAsync(aspire);

        var layoutIdentifier = await CreateAndPublishAsync(layouts, "Line-Edit", camera1);

        var branched = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "draft");
        branched.StatusCode.ShouldBe(HttpStatusCode.Created);
        var draftNumber = await branched.Content.ReadFromJsonAsync<int>();
        draftNumber.ShouldBe(2);

        var edited = await LayoutRequests.PatchAsync(
            layouts, layoutIdentifier, $"revisions/{draftNumber}",
            new
            {
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { new { cameraIdentifier = camera2, overlayIdentifier = (Guid?)null, row = 0, col = 0 } },
            });
        edited.EnsureSuccessStatusCode();

        var published = await LayoutRequests.PostAsync(
            layouts, layoutIdentifier, $"revisions/{draftNumber}/publish");
        published.EnsureSuccessStatusCode();

        var fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.EnsureSuccessStatusCode();
        var payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        var revisions = payload.GetProperty("revisions");
        revisions.GetArrayLength().ShouldBe(2);

        var byNumber = revisions
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
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var layoutIdentifier = await CreateAndPublishAsync(
            layouts, "Line-Revert", await LayoutRequests.RegisterCameraAsync(aspire));

        var reverted = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/revert");
        reverted.EnsureSuccessStatusCode();

        var fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        var payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("revisions")[0].GetProperty("state").GetString().ShouldBe("Draft");
    }

    [Fact]
    public async Task Branching_a_chain_without_a_Published_revision_returns_409()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var created = await layouts.PostAsJsonAsync(
            "/layouts", SingleTileBody($"Drf-{Guid.NewGuid():N}".Substring(0, 16), await LayoutRequests.RegisterCameraAsync(aspire)));
        created.EnsureSuccessStatusCode();
        var layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        var branchAttempt = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "draft");
        branchAttempt.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<Guid> CreateAndPublishAsync(HttpClient layouts, string namePrefix, Guid camera)
    {
        var name = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 16);
        var created = await layouts.PostAsJsonAsync(
            "/layouts", SingleTileBody(name, camera));
        created.EnsureSuccessStatusCode();
        var layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        var published = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/publish");
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
