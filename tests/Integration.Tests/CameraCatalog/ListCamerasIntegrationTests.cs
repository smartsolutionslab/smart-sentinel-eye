using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

[Collection(AspireCollection.Name)]
public class ListCamerasIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_returns_all_registered_cameras_with_default_paging()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        await RegisterAsync(client, "Cam-A", "rtsp://10.0.5.1/h264");
        await RegisterAsync(client, "Cam-B", "rtsp://10.0.5.2/h264");
        await RegisterAsync(client, "Cam-C", "rtsp://10.0.5.3/h264");

        HttpResponseMessage response = await client.GetAsync("/cameras");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("count").GetInt32().ShouldBe(3);
        page.GetProperty("offset").GetInt32().ShouldBe(0);
        page.GetProperty("limit").GetInt32().ShouldBe(50);
        page.GetProperty("items").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task List_applies_offset_and_limit_correctly()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        for (int index = 0; index < 5; index++)
        {
            await RegisterAsync(client, $"Cam-{index:D2}", $"rtsp://10.0.5.{index + 1}/h264");
        }

        HttpResponseMessage response = await client.GetAsync("/cameras?sort=name&order=asc&offset=1&limit=2");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("count").GetInt32().ShouldBe(5);
        page.GetProperty("offset").GetInt32().ShouldBe(1);
        page.GetProperty("limit").GetInt32().ShouldBe(2);
        page.GetProperty("items").GetArrayLength().ShouldBe(2);
        page.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Cam-01");
        page.GetProperty("items")[1].GetProperty("name").GetString().ShouldBe("Cam-02");
    }

    [Fact]
    public async Task List_with_unknown_sort_field_returns_400_with_RFC_7807_problem()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        HttpResponseMessage response = await client.GetAsync("/cameras?sort=created");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("CATALOG_INVALID_SORT_FIELD");
    }

    [Fact]
    public async Task List_without_a_token_returns_401()
    {
        using HttpClient client = aspire.App.CreateHttpClient("camera-catalog");

        HttpResponseMessage response = await client.GetAsync("/cameras");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task RegisterAsync(HttpClient client, string name, string rtspUrl)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/cameras", new { name, rtspUrl });
        response.EnsureSuccessStatusCode();
    }
}
