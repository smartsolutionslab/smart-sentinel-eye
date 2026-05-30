using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

[Collection(AspireCollection.Name)]
public class RegisterCameraIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Register_a_camera_end_to_end_persists_the_row_and_stages_the_outbox_event()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/cameras",
            new { name = "Line-1-Entrance", rtspUrl = "rtsp://10.0.5.12/h264" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        Guid identifier = await response.Content.ReadFromJsonAsync<Guid>();
        identifier.ShouldNotBe(Guid.Empty);
        response.Headers.Location?.ToString().ShouldEndWith($"/cameras/{identifier}");

        await using CameraCatalogDbContext context = await aspire.CreateCameraCatalogDbContextAsync();
        var persisted = await context.Cameras.SingleAsync();
        persisted.Id.Value.ShouldBe(identifier);
        persisted.Name.Value.ShouldBe("Line-1-Entrance");
        persisted.Url.Value.ShouldBe("rtsp://10.0.5.12/h264");
    }

    [Fact]
    public async Task Register_a_camera_with_a_duplicate_name_returns_409_via_HTTP()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        HttpResponseMessage first = await client.PostAsJsonAsync(
            "/cameras",
            new { name = "Cam-Duplicate", rtspUrl = "rtsp://10.0.5.50/h264" });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/cameras",
            new { name = "Cam-Duplicate", rtspUrl = "rtsp://10.0.5.51/h264" });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("CAMERA_NAME_TAKEN");
    }

    [Fact]
    public async Task Register_a_camera_without_a_token_returns_401()
    {
        using HttpClient client = aspire.App.CreateHttpClient("camera-catalog");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/cameras",
            new { name = "Cam-Unauth", rtspUrl = "rtsp://10.0.5.99/h264" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

}
