using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 421 / issue #3023 (owner decision Q1), FR-001, FR-006 and US1, red. <c>POST /cameras</c> and
/// <c>PATCH /cameras/{id}</c> refuse an address whose plain host holds a non-ASCII or invisible character with the
/// existing 400 <c>CAMERA_INVALID_REQUEST</c> whose detail says what to fix; no part of the address is echoed, and no
/// handler is reached. The address is parsed before the version is compared, so a stale If-Match still answers 400. A
/// camera stored with such an address stays editable by name (204): a name-only <c>PATCH</c> never parses
/// <c>rtspUrl</c>; that row passes on arrival and rides along.
/// </summary>
public class CameraUrlNonAsciiHostEndpointTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("rtsp://kamera-m\u00FCnchen.local/live", "m\u00FCnchen")]
    [InlineData("rtsp://cam\u200ESECRET/p", "SECRET")]
    [InlineData("rtsp://SECRET\u3164/p", "SECRET")]
    [InlineData("rtsp://SECRET\u2800:554/p", "SECRET")]
    public async Task Registering_a_camera_at_such_an_address_is_a_400_with_the_fix_and_not_the_address(
        string address,
        string secret)
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        var response = await Send(host, "POST", Register, new InProcessRequest(BodyJson: body));

        AssertRefused(response, address, secret);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("rtsp://kamera-m\u00FCnchen.local/live", "m\u00FCnchen")]
    [InlineData("rtsp://cam\u200ESECRET/p", "SECRET")]
    [InlineData("rtsp://SECRET\u3164/p", "SECRET")]
    [InlineData("rtsp://SECRET\u2800:554/p", "SECRET")]
    public async Task Changing_the_address_of_a_camera_to_such_an_address_is_a_400_and_the_camera_keeps_its_address(
        string address,
        string secret)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, "rtsp://10.0.0.5/stream");

        var response = await SendPatch(host, camera.Id.Value, new { rtspUrl = address }, ifMatch: "0");

        AssertRefused(response, address, secret);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        camera.Version.Value.ShouldBe(0);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Such_an_address_is_refused_before_a_stale_precondition_is_reported()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, "rtsp://10.0.0.5/stream");
        var address = "rtsp://kamera-m00FCnchen.local/live";

        var response = await SendPatch(host, camera.Id.Value, new { rtspUrl = address }, ifMatch: "5");

        AssertRefused(response, address, "m00FCnchen");
    }

    [Fact]
    public async Task A_name_only_change_of_a_camera_stored_with_a_non_ascii_host_is_still_accepted()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, "rtsp://kamera-m00FCnchen.local/live", stored: true);

        var response = await SendPatch(host, camera.Id.Value, new { name = "Dock 2" }, ifMatch: "0");

        response.Status.ShouldBe(204, response.Body);
        camera.Url.Value.ShouldBe("rtsp://kamera-m00FCnchen.local/live");
    }

    private static void AssertRefused(InProcessResponse response, string address, string secret)
    {
        response.Status.ShouldBe(400, response.Body);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(NonAsciiHostRows.NonAsciiHostRefusal);
        response.Body.ShouldNotContain(address, Case.Insensitive);
        response.Body.ShouldNotContain(secret, Case.Insensitive);
    }

    private static async Task<InProcessResponse> Send(
        CameraCatalogEndpointHost host,
        string method,
        string pattern,
        InProcessRequest request)
    {
        try
        {
            return await host.SendAsync(method, pattern, Caller(), request);
        }
        catch (Exception escaped)
        {
            return new InProcessResponse(500, "unhandled " + escaped.GetType().Name, string.Empty);
        }
    }

    private static ClaimsPrincipal Caller() => CameraCatalogEndpointHost.Operator(Subject, "dresden");

    private static Camera Existing(CameraCatalogEndpointHost host, string address, bool stored = false)
    {
        var camera = Camera.Register(
            FabIdentifier.From("dresden"),
            CameraName.From("Dock 1"),
            stored ? RtspUrl.FromStored(address) : RtspUrl.From(address),
            OperatorIdentifier.From(Subject),
            host.Clock.Object);
        host.Cameras
            .Setup(cameras => cameras.GetWithinFabAsync(
                FabIdentifier.From("dresden"), camera.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Camera>.Some(camera));

        return camera;
    }

    private static Task<InProcessResponse> SendPatch(
        CameraCatalogEndpointHost host,
        Guid identifier,
        object body,
        string ifMatch) =>
        Send(
            host,
            "PATCH",
            Patch,
            new InProcessRequest(
                Route: new Dictionary<string, string> { ["camera"] = identifier.ToString() },
                BodyJson: JsonSerializer.Serialize(body, WebOptions),
                Headers: new Dictionary<string, string> { ["If-Match"] = $"\"{ifMatch}\"" }));
}
