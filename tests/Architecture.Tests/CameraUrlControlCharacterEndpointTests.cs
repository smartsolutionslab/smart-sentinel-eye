using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 408 / issue #3031 item 1, FR-001 and US1, red. <c>POST /cameras</c> and <c>PATCH /cameras/{id}</c> refuse
/// an address holding a control character or whitespace anywhere with the existing 400
/// <c>CAMERA_INVALID_REQUEST</c> whose detail says what to fix; neither it nor the rest of the body carries any part
/// of the address, and no handler is reached. The address is parsed before the version is compared, so a stale
/// If-Match still answers 400. An older rule keeps its own text (characterised in
/// <see cref="CameraUrlControlCharacterEndpointCharacterisationTests"/>). An exception that escapes the endpoint is
/// read as the 500 the host would answer with.
/// </summary>
public class CameraUrlControlCharacterEndpointTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private const string ControlOrWhiteSpaceRefusal =
        "value: must not contain a control character or whitespace anywhere: a line break, tab or space is copied into the request a reader sends. Percent-encode a space in a path or query as %20 (Parameter 'value')";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string> Refused => new()
    {
        { "rtsp://10.0.0.66/live\r\nX-Inject: 1", "X-Inject" },
        { "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live", "X-Inject" },
        { "rtsp://10.0.0.66/live?token=a\tSECRET", "SECRET" },
        { "rtsp://10.0.0.66/live\u0000SECRET", "SECRET" },
        { "rtsp://dock-9\x00A0/live", "dock-9" },
        { "rtsp://dock-9/live ", "dock-9" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Registering_a_camera_at_such_an_address_is_a_400_with_the_fix_and_not_the_address(
        string address,
        string secret)
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        var response = await Send(host, "POST", Register, new InProcessRequest(BodyJson: body));

        AssertRefused(response, secret);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Changing_the_address_of_a_camera_to_such_an_address_is_a_400_and_the_camera_keeps_its_address(
        string address,
        string secret)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await SendPatch(host, camera.Id.Value, address, ifMatch: "0");

        AssertRefused(response, secret);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        camera.Version.Value.ShouldBe(0);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Such_an_address_is_refused_before_a_stale_precondition_is_reported()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await SendPatch(host, camera.Id.Value, "rtsp://10.0.0.66/live\r\nX-Inject: 1", ifMatch: "5");

        AssertRefused(response, "X-Inject");
    }

    private static void AssertRefused(InProcessResponse response, string secret)
    {
        response.Status.ShouldBe(400, response.Body);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(ControlOrWhiteSpaceRefusal);
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
        Send(
            host,
            "PATCH",
            Patch,
            new InProcessRequest(
                Route: new Dictionary<string, string> { ["camera"] = identifier.ToString() },
                BodyJson: JsonSerializer.Serialize(new { rtspUrl = address }, WebOptions),
                Headers: new Dictionary<string, string> { ["If-Match"] = $"\"{ifMatch}\"" }));
}
