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
    private readonly AspireFixture _aspire = aspire;

    public async Task InitializeAsync()
    {
        await _aspire.ResetLayoutCompositionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publishing_a_new_revision_atomically_archives_the_previous_Published_revision()
    {
        using HttpClient layouts = await _aspire.CreateAdminClientAsync("layout-composition");
        Guid camera1 = Guid.CreateVersion7();
        Guid camera2 = Guid.CreateVersion7();

        Guid layoutIdentifier = await CreateAndPublishAsync(layouts, "Line-Edit", camera1);

        HttpResponseMessage branched = await layouts.PostAsync(
            $"/layouts/{layoutIdentifier}/draft", content: null);
        branched.StatusCode.ShouldBe(HttpStatusCode.Created);
        int draftNumber = await branched.Content.ReadFromJsonAsync<int>();
        draftNumber.ShouldBe(2);

        HttpResponseMessage edited = await layouts.PatchAsJsonAsync(
            $"/layouts/{layoutIdentifier}/revisions/{draftNumber}",
            new { cameraIdentifier = camera2 });
        edited.EnsureSuccessStatusCode();

        HttpResponseMessage published = await layouts.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/{draftNumber}/publish", content: null);
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
        byNumber[2].GetProperty("cameraIdentifier").GetGuid().ShouldBe(camera2);
    }

    [Fact]
    public async Task Reverting_a_Published_revision_brings_it_back_to_Draft()
    {
        using HttpClient layouts = await _aspire.CreateAdminClientAsync("layout-composition");
        Guid layoutIdentifier = await CreateAndPublishAsync(
            layouts, "Line-Revert", Guid.CreateVersion7());

        HttpResponseMessage reverted = await layouts.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/revert", content: null);
        reverted.EnsureSuccessStatusCode();

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("revisions")[0].GetProperty("state").GetString().ShouldBe("Draft");
    }

    [Fact]
    public async Task Branching_a_chain_without_a_Published_revision_returns_409()
    {
        using HttpClient layouts = await _aspire.CreateAdminClientAsync("layout-composition");
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", new { name = $"Drf-{Guid.NewGuid():N}".Substring(0, 16), cameraIdentifier = Guid.CreateVersion7() });
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage branchAttempt = await layouts.PostAsync(
            $"/layouts/{layoutIdentifier}/draft", content: null);
        branchAttempt.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<Guid> CreateAndPublishAsync(HttpClient layouts, string namePrefix, Guid camera)
    {
        string name = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 16);
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", new { name, cameraIdentifier = camera });
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage published = await layouts.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/publish", content: null);
        published.EnsureSuccessStatusCode();
        return layoutIdentifier;
    }
}
