using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 389 / issue #2999, US2, characterisation (ADR-0144), captured green before the write path starts refusing a
/// raw '@' after the authority. A camera registered before that rule can sit in <c>cameras.rtsp_url</c> with an
/// address the rule would now refuse. Reading it must keep working through the running service, not only through
/// the EF value converter: a read that throws takes the whole fab's camera list down.
///
/// <para>
/// The row is inserted by raw SQL because the API will no longer accept the address that produced it. The value is
/// returned as stored; masking it in the response is a separate issue (#2999 decision item 5).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StoredMisreadCameraAddressIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string MunichOperatorPassword = SeededCredentials.Op3Munich;
    private const string MisreadAddress = "rtsp://admin:12345#@cam/s";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_camera_stored_with_a_misread_address_is_read_back_by_identifier()
    {
        var (camera, _) = await InsertAsync();
        using var cameras = await aspire.CreateAuthenticatedClientAsync("camera-catalog", MunichOperator, MunichOperatorPassword);

        var response = await cameras.GetAsync($"/cameras/{camera}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rtspUrl").GetString().ShouldBe(MisreadAddress);
    }

    [Fact]
    public async Task A_camera_stored_with_a_misread_address_is_listed_with_the_fab()
    {
        var (_, name) = await InsertAsync();
        using var cameras = await aspire.CreateAuthenticatedClientAsync("camera-catalog", MunichOperator, MunichOperatorPassword);

        var response = await cameras.GetAsync("/cameras?limit=200");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var listed = page.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("name").GetString() == name);
        listed.GetProperty("rtspUrl").GetString().ShouldBe(MisreadAddress);
    }

    private async Task<(Guid Camera, string Name)> InsertAsync()
    {
        var camera = Guid.CreateVersion7();
        var name = $"misread-{camera:N}"[..24];
        await using var db = await aspire.CreateCameraCatalogDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO cameras (camera_id, fab, name, rtsp_url, status, version, registered_at, registered_by)
            VALUES ({0}, 'munich', {1}, {2}, 'Registered', 0, {3}, {4});
            """,
            camera,
            name,
            MisreadAddress,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7());

        return (camera, name);
    }
}
