using System.Net.Http.Headers;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Integration.Tests.Identity;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 438 US2 (#3016, ADR-0179) - the camera read and the camera listing serve the stored address as its
/// origin to every caller, including a principal holding only <c>sse.cameras.read</c> (the shape a kiosk token
/// has). The full address is read only through <c>GET /cameras/{camera}/address</c>.
///
/// <para>
/// <b>Red on unchanged code:</b> both reads copy the stored address into the body, path and query included.
/// Each fact carries a positive control (the writer's address route still returns the full address), so a
/// missing registration cannot pass as masking.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CameraReadsServeOriginIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string MunichOperatorPassword = SeededCredentials.Op3Munich;
    private const string ReadOnlyUser = "operator";
    private const string ReadOnlyPassword = SeededCredentials.Operator;
    private const string Origin = "rtsp://10.0.5.98:8554";

    private static readonly string[] ReadOnlyScopes =
        ["sse-identity", "sse-audience", "sse-groups", "sse.cameras.read"];

    private readonly RealmProbe realm = new(aspire);

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reading_one_camera_serves_the_origin_and_no_path_or_query()
    {
        var marker = Marker();
        using var writer = await ClientFor();
        var camera = await RegisterAsync(writer, marker);
        await AssertFullAddressStillReadableByTheWriterAsync(writer, camera, marker);

        var response = await writer.GetAsync($"/cameras/{camera}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("rtspUrl").GetString().ShouldBe(Origin);
        text.ShouldNotContain(marker);
    }

    [Fact]
    public async Task Listing_serves_the_origin_and_no_path_or_query()
    {
        var marker = Marker();
        using var writer = await ClientFor();
        var camera = await RegisterAsync(writer, marker);
        await AssertFullAddressStillReadableByTheWriterAsync(writer, camera, marker);

        var response = await writer.GetAsync("/cameras?limit=200");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        using var page = JsonDocument.Parse(text);
        page.RootElement.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("cameraIdentifier").GetGuid() == camera)
            .GetProperty("rtspUrl").GetString().ShouldBe(Origin);
        text.ShouldNotContain(marker);
    }

    [Fact]
    public async Task A_caller_holding_only_the_read_scope_gets_the_listing_and_the_camera_without_the_path_or_query()
    {
        var marker = Marker();
        using var writer = await ClientFor();
        var camera = await RegisterAsync(writer, marker);
        await AssertFullAddressStillReadableByTheWriterAsync(writer, camera, marker);

        var clientId = $"camera-origin-probe-{Guid.CreateVersion7():N}";
        await realm.PlantPasswordGrantClientAsync(clientId, ReadOnlyScopes, CancellationToken.None);

        HttpResponseMessage listing;
        HttpResponseMessage one;
        try
        {
            using var readOnly = aspire.CreateServiceClient("camera-catalog");
            readOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await aspire.GetAccessTokenForClientAsync(clientId, ReadOnlyUser, ReadOnlyPassword, "openid"));
            listing = await readOnly.GetAsync("/cameras?limit=200");
            one = await readOnly.GetAsync($"/cameras/{camera}");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }

        // The reads themselves must succeed for the principal: a refusal would hold no marker either.
        listing.StatusCode.ShouldBe(HttpStatusCode.OK, await listing.Content.ReadAsStringAsync());
        one.StatusCode.ShouldBe(HttpStatusCode.OK, await one.Content.ReadAsStringAsync());

        var listingText = await listing.Content.ReadAsStringAsync();
        var oneText = await one.Content.ReadAsStringAsync();
        listingText.ShouldContain(Origin);
        oneText.ShouldContain(Origin);
        listingText.ShouldNotContain(marker);
        oneText.ShouldNotContain(marker);
    }

    private static async Task AssertFullAddressStillReadableByTheWriterAsync(HttpClient writer, Guid camera, string marker)
    {
        var control = await writer.GetAsync($"/cameras/{camera}/address");

        control.StatusCode.ShouldBe(HttpStatusCode.OK, "the positive control failed: the writer cannot read the address");
        (await control.Content.ReadAsStringAsync()).ShouldContain(marker);
    }

    private async Task<HttpClient> ClientFor() =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", MunichOperator, MunichOperatorPassword);

    private static string Marker() => $"PWMARK-{Guid.CreateVersion7():N}";

    private static async Task<Guid> RegisterAsync(HttpClient cameras, string marker)
    {
        var created = await cameras.PostAsJsonAsync(
            "/cameras",
            new { name = $"origin-{Guid.CreateVersion7():N}"[..24], rtspUrl = $"{Origin}/stream/{marker}?token={marker}" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
