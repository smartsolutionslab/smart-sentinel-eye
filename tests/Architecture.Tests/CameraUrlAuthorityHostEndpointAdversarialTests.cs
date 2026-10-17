using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 392 / issues #3012 and #3017, adversarial (T070). Through <c>POST /cameras</c> and
/// <c>PATCH /cameras/{id}</c>: an empty host behind a slash run or odd casing and a bracket tail hiding a number
/// are answered 400 with the rule's text and no part of the address in the body, before a stale precondition is
/// reported; and addresses that registered before the change (an IPv6 host with a port, uppercase hex, a zone, a
/// '[' or ']' in the path or query) still register and still re-point.
/// </summary>
public class CameraUrlAuthorityHostEndpointAdversarialTests
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
        { "RtSp:////admin:4242@cam/s", "4242", EmptyHostRefusal },
        { "rtsp://////admin:4242@cam/s", "4242", EmptyHostRefusal },
        { "rtsp:///[fe80::1]admin:4242/x", "4242", EmptyHostRefusal },
        { "rtsp://[fe80::1]x:4242/s", "4242", BracketTailRefusal },
        { "rtsp://[fe80::1]%3A4242/s", "4242", BracketTailRefusal },
        { "rtsp://@[fe80::1]x4242/s", "4242", BracketTailRefusal },
        { "rtsp://[fe80::1]\u00A04242/s", "4242", BracketTailRefusal },
    };

    public static TheoryData<string> StillAccepted =>
    [
        "rtsp://[::1]:8554/a",
        "rtsp://[FE80::ABCD]:8554/s",
        "rtsp://[fe80::1%25eth0]:554/s",
        "rtsp://[::ffff:10.0.0.1]:554/x",
        "rtsp://host:554/[x]",
        "rtsp://cam/p]q",
        "rtsp://[::1]?a]b",
    ];

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Registering_at_such_an_address_is_refused_with_the_rule_and_none_of_the_address(
        string address,
        string secret,
        string refusal)
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, address);

        AssertRefused(response, secret, refusal);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Re_pointing_to_such_an_address_with_a_stale_version_is_refused_on_the_address_not_the_version(
        string address,
        string secret,
        string refusal)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await SendPatch(host, camera.Id.Value, address, ifMatch: "7");

        AssertRefused(response, secret, refusal);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(StillAccepted))]
    public async Task An_address_that_registered_before_the_change_still_registers(string address)
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, address);

        response.Status.ShouldBe(201);
        host.Cameras.Verify(cameras => cameras.Add(It.Is<Camera>(camera => camera.Url.Value == address)), Times.Once);
    }

    [Theory]
    [MemberData(nameof(StillAccepted))]
    public async Task An_address_that_re_pointed_before_the_change_still_re_points(string address)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await SendPatch(host, camera.Id.Value, address, ifMatch: "0");

        response.Status.ShouldBe(204);
        camera.Url.Value.ShouldBe(address);
    }

    private static void AssertRefused(InProcessResponse response, string secret, string refusal)
    {
        response.Status.ShouldBe(400);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(refusal);
        response.Body.ShouldNotContain(secret, Case.Insensitive);
    }

    private static Task<InProcessResponse> Post(CameraCatalogEndpointHost host, string address) =>
        host.SendAsync(
            "POST",
            Register,
            Caller(),
            new InProcessRequest(
                BodyJson: JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions)));

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
