using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 450 / issue #3118, phase 4a RED (new behaviour). A camera registered before the userinfo rule can sit in
/// <c>cameras.rtsp_url</c> holding a credential. Reading it must mask it, not throw: otherwise both reads answer 500
/// because the aggregate cannot be materialised. The row is inserted by raw SQL because the API refuses the address.
/// The marker is synthetic.
/// </summary>
[Collection(AspireCollection.Name)]
public class StoredCredentialCameraAddressIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string MunichOperatorPassword = SeededCredentials.Op3Munich;
    private const string Marker = "USERPWMARK";
    private const string Origin = "rtsp://10.0.5.98";
    private const string StoredAddress = "rtsp://u:" + Marker + "@10.0.5.98/x";
    private const string Corrected = "rtsp://10.0.5.98/corrected";
    private const string PlainAddress = "rtsp://10.0.5.99/s";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reading_a_camera_stored_with_a_credential_serves_the_origin_and_no_marker()
    {
        var (camera, _) = await InsertAsync();
        using var cameras = await ClientAsync();

        var response = await cameras.GetAsync($"/cameras/{camera}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("rtspUrl").GetString().ShouldBe(Origin);
        text.ShouldNotContain(Marker);
        (await StoredAddressAsync(camera)).ShouldBe(StoredAddress);
    }

    [Fact]
    public async Task Listing_cameras_with_one_stored_with_a_credential_serves_the_origin_and_no_marker()
    {
        var (camera, name) = await InsertAsync();
        var (_, plainName) = await InsertAsync(PlainAddress);
        using var cameras = await ClientAsync();

        var response = await cameras.GetAsync("/cameras?limit=200");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var text = await response.Content.ReadAsStringAsync();
        using var page = JsonDocument.Parse(text);
        var items = page.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Single(row => row.GetProperty("name").GetString() == name)
            .GetProperty("rtspUrl").GetString().ShouldBe(Origin);
        items.ShouldContain(row => row.GetProperty("name").GetString() == plainName);
        text.ShouldNotContain(Marker);
        (await StoredAddressAsync(camera)).ShouldBe(StoredAddress);
    }

    [Fact]
    public async Task The_writer_only_address_route_returns_the_full_stored_address()
    {
        var (camera, _) = await InsertAsync();
        using var cameras = await ClientAsync();

        var response = await cameras.GetAsync($"/cameras/{camera}/address");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rtspUrl").GetString().ShouldBe(StoredAddress);
    }

    [Fact]
    public async Task A_patch_corrects_a_camera_stored_with_a_credential()
    {
        var (camera, _) = await InsertAsync();
        using var cameras = await ClientAsync();
        HttpRequestMessage request = new(HttpMethod.Patch, $"/cameras/{camera}")
        {
            Content = new StringContent($"{{\"rtspUrl\":\"{Corrected}\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"0\""));

        var response = await cameras.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await StoredAddressAsync(camera)).ShouldBe(Corrected);
        (await AuditPayloadAsync(camera)).ShouldNotContain(Marker);
    }

    private async Task<string> AuditPayloadAsync(Guid camera)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var db = await aspire.CreateAuditObservabilityDbContextAsync();

            var rows = await db.Database
                .SqlQuery<string>($"""
                    SELECT payload::text AS "Value"
                    FROM audit_events
                    WHERE event_kind = 'CameraAddressChangedV1'
                      AND payload->>'Camera' = {camera.ToString()}
                    """)
                .ToListAsync();

            if (rows.Count > 0)
            {
                return rows[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"No CameraAddressChangedV1 audit row for camera {camera} within 30s.");
    }

    private async Task<HttpClient> ClientAsync() =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", MunichOperator, MunichOperatorPassword);

    private async Task<string> StoredAddressAsync(Guid camera)
    {
        await using var db = await aspire.CreateCameraCatalogDbContextAsync();

        var stored = await db.Database
            .SqlQuery<string>($"""SELECT rtsp_url AS "Value" FROM cameras WHERE camera_id = {camera}""")
            .ToListAsync();

        return stored.ShouldHaveSingleItem();
    }

    private async Task<(Guid Camera, string Name)> InsertAsync(string address = StoredAddress)
    {
        var camera = Guid.CreateVersion7();
        var name = $"cred-{camera:N}"[..24];
        await using var db = await aspire.CreateCameraCatalogDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO cameras (camera_id, fab, name, rtsp_url, status, version, registered_at, registered_by)
            VALUES ({0}, 'munich', {1}, {2}, 'Registered', 0, {3}, {4});
            """,
            camera,
            name,
            address,
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7());

        return (camera, name);
    }
}
