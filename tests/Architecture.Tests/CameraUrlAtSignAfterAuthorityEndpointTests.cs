using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 389 / issue #2999, red. <c>POST /cameras</c> and <c>PATCH /cameras/{id}</c> refuse an address with a raw
/// '@' after the authority, because the parser would read what precedes it (often a camera password) as host and
/// port. The refusal is the existing 400 <c>CAMERA_INVALID_REQUEST</c>; its detail says what to fix and neither
/// it nor the rest of the body carries any part of the address. The real handlers run behind the real endpoints;
/// no handler is reached.
/// </summary>
public class CameraUrlAtSignAfterAuthorityEndpointTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private const string Refusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("rtsp://admin:12345#@cam/s", "12345")]
    [InlineData("rtsp://admin:4711?@cam/s", "4711")]
    [InlineData("rtsp://admin:8080/x@cam/s", "admin")]
    [InlineData("RTSP://ADMIN:12345#@CAM", "12345")]
    [InlineData("rtsp://3232235777:12345#@cam", "3232235777")]
    public async Task Registering_a_camera_at_a_misread_address_is_refused_with_the_fix_and_not_the_address(
        string address,
        string secret)
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        var response = await host.SendAsync("POST", Register, Caller(), new InProcessRequest(BodyJson: body));

        AssertRefused(response, secret);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("rtsp://admin:12345#@cam/s", "12345")]
    [InlineData("rtsp://admin:4711?@cam/s", "4711")]
    [InlineData("rtsp://admin:8080/x@cam/s", "admin")]
    [InlineData("RTSP://ADMIN:12345#@CAM", "12345")]
    [InlineData("rtsp://3232235777:12345#@cam", "3232235777")]
    public async Task Changing_the_address_of_a_camera_to_a_misread_one_is_refused_and_the_camera_keeps_its_address(
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
    public async Task A_misread_address_is_refused_before_a_stale_precondition_is_reported()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await SendPatch(host, camera.Id.Value, "rtsp://admin:12345#@cam/s", ifMatch: "5");

        AssertRefused(response, "12345");
    }

    private static void AssertRefused(InProcessResponse response, string secret)
    {
        response.Status.ShouldBe(400);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(Refusal);
        response.Body.ShouldNotContain(secret, Case.Insensitive);
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
