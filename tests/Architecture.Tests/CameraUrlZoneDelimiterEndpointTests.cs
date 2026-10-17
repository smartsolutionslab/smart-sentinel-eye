using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 394 / issue #3023 items 1, 2 and 5, red. <c>POST /cameras</c> and <c>PATCH /cameras/{id}</c> refuse an
/// address with '@', '?' or '#' inside the IPv6 brackets, and answer 400 (not 500) for an address whose host or
/// userinfo the parser cannot read. The refusal is the existing 400 <c>CAMERA_INVALID_REQUEST</c> whose detail says
/// what to fix; neither it nor the rest of the body carries any part of the address, and no handler is reached.
/// An exception that escapes the endpoint is read as the 500 the host would answer with.
/// </summary>
public class CameraUrlZoneDelimiterEndpointTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private const string DelimiterRefusal =
        "value: must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host (Parameter 'value')";

    private const string UnreadableHostRefusal =
        "value: must name a host the parser can read: the address inside the brackets is malformed, for example a ':' left before a '/' prefix length (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string, string> Refused => new()
    {
        { "rtsp://@[fe80::1%@10.0.0.66:8554]/live", "10.0.0.66", DelimiterRefusal },
        { "rtsp://@[::1%25@evil.com:554]/s", "evil.com", DelimiterRefusal },
        { "rtsp://[::1%25x?y]/s", "x?y", DelimiterRefusal },
        { "rtsp://[fe80::1%25eth0#]:554/s", "eth0#", DelimiterRefusal },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]", "1:2:3:4", UnreadableHostRefusal },
        { "rtsp://[::1:2:3:4:5:6:7:/64]/s", "1:2:3:4", UnreadableHostRefusal },
        { "RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s", "1:2:3:4", UserInfoRefusal },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Registering_a_camera_at_such_an_address_is_a_400_with_the_fix_and_not_the_address(
        string address,
        string secret,
        string refusal)
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        var response = await Send(host, "POST", Register, new InProcessRequest(BodyJson: body));

        AssertRefused(response, secret, refusal);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Changing_the_address_of_a_camera_to_such_an_address_is_a_400_and_the_camera_keeps_its_address(
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

    [Theory]
    [InlineData("rtsp://@[fe80::1%@10.0.0.66:8554]/live", "10.0.0.66", DelimiterRefusal)]
    [InlineData("rtsp://[1:2:3:4:5:6:7:8:/64]", "1:2:3:4", UnreadableHostRefusal)]
    public async Task Such_an_address_is_refused_before_a_stale_precondition_is_reported(
        string address,
        string secret,
        string refusal)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await SendPatch(host, camera.Id.Value, address, ifMatch: "5");

        AssertRefused(response, secret, refusal);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1%25eth0]/x")]
    [InlineData("rtsp://[fe80::1/64%25eth0]/s")]
    [InlineData("rtsp://@[fe80::1]/x")]
    [InlineData("rtsp://[::1%25%40x]/s")]
    public async Task An_ordinary_or_zoned_address_is_still_registered(string address)
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions);

        var response = await Send(host, "POST", Register, new InProcessRequest(BodyJson: body));

        response.Status.ShouldBe(201, response.Body);
    }

    private static void AssertRefused(InProcessResponse response, string secret, string refusal)
    {
        response.Status.ShouldBe(400, response.Body);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(refusal);
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
