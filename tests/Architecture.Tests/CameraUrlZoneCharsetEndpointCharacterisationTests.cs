using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Characterisation (spec 411 / issue #3023, ADR-0144), captured green on <c>e5be6d72</c> and required to stay green
/// after. An ASCII zone and a host with a trailing dot or an underscore are still registered (201). A camera stored
/// before the change with a now-refused address stays editable by name: a name-only <c>PATCH</c> never parses
/// <c>rtspUrl</c> (spec 411 §1.8), so it answers 204. None of these rows may be edited by the change.
/// </summary>
public class CameraUrlZoneCharsetEndpointCharacterisationTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("rtsp://[fe80::1%25eth0]:8554/x")]
    [InlineData("rtsp://[fe80::1%2512]/s")]
    [InlineData("rtsp://[fe80::1%25eth0.100]/s")]
    [InlineData("rtsp://[::1%25%40x]/s")]
    [InlineData("rtsp://cam./x")]
    [InlineData("rtsp://cam_1/x")]
    public async Task An_ascii_zone_and_an_ordinary_host_are_still_registered(string address)
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        var response = await host.SendAsync("POST", Register, Caller(), new InProcessRequest(BodyJson: body));

        response.Status.ShouldBe(201, response.Body);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live")]
    [InlineData("rtsp://..")]
    public async Task A_name_only_change_of_a_camera_stored_with_a_now_refused_address_is_still_accepted(string stored)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Camera.Register(
            FabIdentifier.From("dresden"),
            CameraName.From("Dock 1"),
            RtspUrl.FromStored(stored),
            OperatorIdentifier.From(Subject),
            host.Clock.Object);
        host.Cameras
            .Setup(cameras => cameras.GetWithinFabAsync(
                FabIdentifier.From("dresden"), camera.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Camera>.Some(camera));

        var response = await host.SendAsync(
            "PATCH",
            Patch,
            Caller(),
            new InProcessRequest(
                Route: new Dictionary<string, string> { ["camera"] = camera.Id.Value.ToString() },
                BodyJson: JsonSerializer.Serialize(new { name = "Dock 2" }, WebOptions),
                Headers: new Dictionary<string, string> { ["If-Match"] = "\"0\"" }));

        response.Status.ShouldBe(204, response.Body);
        camera.Url.Value.ShouldBe(stored);
    }

    private static ClaimsPrincipal Caller() => CameraCatalogEndpointHost.Operator(Subject, "dresden");
}
