using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 414 / issue #3031 item 3, FR-001 and US1, red. <c>POST /cameras</c> and <c>PATCH /cameras/{id}</c> refuse an
/// address with a ':' after the host and no port with the existing 400 <c>CAMERA_INVALID_REQUEST</c> whose detail says
/// what to fix, and no handler is reached. The address is parsed before the version is compared, so a stale If-Match
/// still answers 400. A camera stored with such an address stays editable by name (204): a name-only <c>PATCH</c>
/// never parses <c>rtspUrl</c>; that row passes on arrival and rides along.
/// </summary>
public class CameraUrlEmptyPortEndpointTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private const string EmptyPortRefusal =
        "value: must write a port after the ':' that follows the host, or leave the ':' out: an empty port is read as the default port by some readers and as port 0 by others (Parameter 'value')";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("rtsp://10.0.0.66:/live")]
    [InlineData("rtsp://[::1]:/live")]
    public async Task Registering_a_camera_at_such_an_address_is_a_400_with_the_fix_and_not_the_address(string address)
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        var response = await Send(host, "POST", Register, new InProcessRequest(BodyJson: body));

        AssertRefused(response, address);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("rtsp://10.0.0.66:/live")]
    [InlineData("rtsp://[::1]:/live")]
    public async Task Changing_the_address_of_a_camera_to_such_an_address_is_a_400_and_the_camera_keeps_its_address(
        string address)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, "rtsp://10.0.0.5/stream");

        var response = await SendPatch(host, camera.Id.Value, new { rtspUrl = address }, ifMatch: "0");

        AssertRefused(response, address);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        camera.Version.Value.ShouldBe(0);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Such_an_address_is_refused_before_a_stale_precondition_is_reported()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, "rtsp://10.0.0.5/stream");

        var response = await SendPatch(host, camera.Id.Value, new { rtspUrl = "rtsp://10.0.0.66:/live" }, ifMatch: "5");

        AssertRefused(response, "rtsp://10.0.0.66:/live");
    }

    [Fact]
    public async Task A_name_only_change_of_a_camera_stored_with_an_empty_port_is_still_accepted()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, "rtsp://10.0.0.66:/live", stored: true);

        var response = await SendPatch(host, camera.Id.Value, new { name = "Dock 2" }, ifMatch: "0");

        response.Status.ShouldBe(204, response.Body);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.66:/live");
    }

    private static void AssertRefused(InProcessResponse response, string address)
    {
        response.Status.ShouldBe(400, response.Body);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(EmptyPortRefusal);
        response.Body.ShouldNotContain(address, Case.Insensitive);
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
