using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Phase-6 remediation on spec 258 US1: <c>ux_walls_fab_name_ci</c> is a
/// case-insensitive unique index on <c>(fab, lower(name))</c>, but
/// <c>WallRepository.FindByNameAsync</c> used to compare names with ordinal
/// equality. A name differing only in case from an existing wall passed the
/// application-level check and then failed the database constraint,
/// surfacing as a generic <c>409 RESOURCE_ALREADY_EXISTS</c> instead of the
/// typed <c>WALL_NAME_TAKEN</c> every other duplicate-name case gets.
/// </summary>
[Collection(AspireCollection.Name)]
public class WallNameCaseInsensitivityIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public Task InitializeAsync() => aspire.ResetLayoutCompositionAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_wall_name_differing_only_in_case_from_an_existing_one_is_refused_with_WALL_NAME_TAKEN()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var original = $"Line-{Guid.NewGuid():N}"[..16];
        var differingOnlyInCase = original.ToUpperInvariant();

        (await CreateWallAsync(layouts, original, [a, b])).StatusCode.ShouldBe(HttpStatusCode.Created);

        var refused = await CreateWallAsync(layouts, differingOnlyInCase, [a, b]);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("WALL_NAME_TAKEN");
    }

    private async Task<Guid> PublishedLayoutAsync(HttpClient layouts)
    {
        var name = $"W-{Guid.NewGuid():N}"[..16];
        var camera = await LayoutRequests.RegisterCameraAsync(aspire);

        var created = await layouts.PostAsJsonAsync("/layouts", new
        {
            name,
            grid = new { rows = 1, cols = 1 },
            tiles = new[]
            {
                new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 },
            },
        });
        created.EnsureSuccessStatusCode();
        var layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        var published = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        return layoutIdentifier;
    }

    private static Task<HttpResponseMessage> CreateWallAsync(HttpClient layouts, string name, IReadOnlyList<Guid> scenes) =>
        layouts.PostAsJsonAsync("/walls", new { name, scenes });

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}";
}
