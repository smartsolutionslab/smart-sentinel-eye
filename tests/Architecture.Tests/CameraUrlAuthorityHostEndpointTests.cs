using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 392 / issues #3012 and #3017, red. <c>POST /cameras</c> and <c>PATCH /cameras/{id}</c> refuse an address
/// whose host is empty (a password written after a third '/' would be stored in clear) and an address with text
/// after the closing ']' of an IPv6 host. The refusal is the existing 400 <c>CAMERA_INVALID_REQUEST</c> whose
/// detail says what to fix; neither it nor the rest of the body carries any part of the address, and no handler
/// is reached.
/// </summary>
public class CameraUrlAuthorityHostEndpointTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private const string EmptyHostRefusal =
        "value: must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string, string> Refused => new()
    {
        { "rtsp:///admin:4242@cam/s", "4242", EmptyHostRefusal },
        { "rtsp:///cam", "n/a", EmptyHostRefusal },
        { "rtsp://[fe80::1]%25eth0/x", "eth0", BracketTailRefusal },
        { "rtsp://[fe80::1]garbage.example/x", "garbage.example", BracketTailRefusal },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Registering_a_camera_at_such_an_address_is_refused_with_the_fix_and_not_the_address(
        string address,
        string secret,
        string refusal)
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        var response = await host.SendAsync("POST", Register, Caller(), new InProcessRequest(BodyJson: body));

        AssertRefused(response, secret, refusal);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Changing_the_address_of_a_camera_to_such_an_address_is_refused_and_the_camera_keeps_its_address(
        string address,
        string secret,
        string refusal)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await SendPatch(host, camera.Id.Value, address, ifMatch: "0");

        AssertRefused(response, secret, refusal);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        camera.Version.Value.ShouldBe(0);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Such_an_address_is_refused_before_a_stale_precondition_is_reported()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await SendPatch(host, camera.Id.Value, "rtsp:///admin:4242@cam/s", ifMatch: "5");

        AssertRefused(response, "4242", EmptyHostRefusal);
    }

    private static void AssertRefused(InProcessResponse response, string secret, string refusal)
    {
        response.Status.ShouldBe(400);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(refusal);
        if (secret != "n/a")
        {
            response.Body.ShouldNotContain(secret, Case.Insensitive);
        }
    }

    private static ClaimsPrincipal Caller() => CameraCatalogEndpointHost.Operator(Subject, "dresden");

    private static Camera Existing(CameraCatalogEndpointHost host)
    {
        var camera = Camera.Register(
            FabIdentifier.From("dresden"),
            CameraName.From("Dock 1"),
            RtspUrl.From("rtsp://10.0.0.5/stream"),
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
        string address,
        string ifMatch) =>
        host.SendAsync(
            "PATCH",
            Patch,
            Caller(),
            new InProcessRequest(
                Route: new Dictionary<string, string> { ["camera"] = identifier.ToString() },
                BodyJson: JsonSerializer.Serialize(new { rtspUrl = address }, WebOptions),
                Headers: new Dictionary<string, string> { ["If-Match"] = $"\"{ifMatch}\"" }));
}
