using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 392 / issue #3012, adversarial finding (T070), red against the fix. An address whose text after the closing
/// ']' holds an '@' is registered and re-pointed today: the parser reads host <c>[fe80::1]</c> and moves
/// <c>admin:4242@cam</c> into the path, so the userinfo rule passes, and
/// <c>UrlAuthority.TextFollowsTheClosingBracket</c> ends the userinfo at the last '@' and never sees the bracket. A
/// password written that way is stored, published on the bus and returned by the API. Each must be answered 400
/// <c>CAMERA_INVALID_REQUEST</c> with no part of the address in the body, and nothing saved.
/// </summary>
public class CameraUrlAuthorityHostEndpointAdversarialFindingTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> AtSignAfterTheClosingBracket =>
    [
        "rtsp://@[fe80::1]admin:4242@cam/s",
        "rtsp://[fe80::1]admin:4242@",
    ];

    [Theory]
    [MemberData(nameof(AtSignAfterTheClosingBracket))]
    public async Task Registering_at_an_address_with_an_at_sign_after_the_closing_bracket_is_refused_and_nothing_is_stored(
        string address)
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await host.SendAsync(
            "POST",
            Register,
            Caller(),
            new InProcessRequest(
                BodyJson: JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions)));

        AssertRefused(response);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(AtSignAfterTheClosingBracket))]
    public async Task Re_pointing_to_an_address_with_an_at_sign_after_the_closing_bracket_is_refused_and_the_camera_keeps_its_address(
        string address)
    {
        await using var host = new CameraCatalogEndpointHost();
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

        var response = await host.SendAsync(
            "PATCH",
            Patch,
            Caller(),
            new InProcessRequest(
                Route: new Dictionary<string, string> { ["camera"] = camera.Id.Value.ToString() },
                BodyJson: JsonSerializer.Serialize(new { rtspUrl = address }, WebOptions),
                Headers: new Dictionary<string, string> { ["If-Match"] = "\"0\"" }));

        AssertRefused(response);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertRefused(InProcessResponse response)
    {
        response.Status.ShouldBe(400);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        response.Body.ShouldNotContain("4242", Case.Insensitive);
    }

    private static ClaimsPrincipal Caller() => CameraCatalogEndpointHost.Operator(Subject, "dresden");
}
