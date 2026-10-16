using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 389 / issue #2999, adversarial (T050), on the in-process CameraCatalog host. Pins three things the
/// written endpoint tests leave open. First, the variants beyond the issue's five forms are refused on both write
/// paths with a body that carries none of the address. Second, spec A-3: a keyed retry of a registration whose
/// address is now refused answers 400 before the idempotency store is consulted, even when the store holds a
/// completed answer for that key, so it never replays the original 201. Third, a camera whose stored address the
/// new rule refuses (read back through <c>FromStored</c>) can still be renamed and corrected; re-sending its own
/// stored address is refused like any other write.
/// </summary>
public class CameraUrlRefusalEndpointAdversarialTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private const string MisreadAddress = "rtsp://admin:4242#@cam/s";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000002");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string> Variants => new()
    {
        { "RtSp://admin:4242#@cam", "4242" },
        { "rtsp://cam/s?u=admin:4242@x", "4242" },
        { "rtsp://cam/s#admin:4242@x", "4242" },
        { "rtsp://cam/s\\admin:4242@x", "4242" },
        { "rtsp://admin:4242#@x@cam/s", "4242" },
        { "rtsp://admin:4242#@[fd00::5]:8554/x", "4242" },
        { "rtsp://admin:4242#@cam/s ", "4242" },
        { "rtsp://admin:4242#\t@cam", "4242" },
        { "rtsp:///admin:4242#@cam", "4242" },
        { "rtsp://admin:4242\\@cam/s", "4242" },
        { "rtsp://admin:4242@cam/s", "4242" },
        { "rtsp://admin:4242%23@cam/s", "4242" },
        { " rtsp://admin:4242#@cam", "4242" },
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task Registering_a_variant_is_refused_and_the_body_carries_none_of_it(string address, string secret)
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await PostRegistration(host, address, key: null);

        AssertRefusedWithout(response, secret);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task Changing_an_address_to_a_variant_is_refused_and_the_body_carries_none_of_it(string address, string secret)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, RtspUrl.From("rtsp://10.0.0.5/stream"));

        var response = await SendPatch(host, camera.Id.Value, new { rtspUrl = address });

        AssertRefusedWithout(response, secret);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_keyed_retry_of_a_now_refused_registration_answers_400_without_consulting_the_store()
    {
        await using var host = new CameraCatalogEndpointHost();
        host.IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyReservation.CompletedWith(new Guid("0197a1b2-0000-7000-8000-0000000000aa")));

        var response = await PostRegistration(host, MisreadAddress, key: "retry-after-deploy");

        AssertRefusedWithout(response, "4242");
        response.Location.ShouldBeEmpty();
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
    }

    [Fact]
    public async Task A_camera_stored_at_a_now_refused_address_can_still_be_renamed()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, RtspUrl.FromStored(MisreadAddress));

        var response = await SendPatch(host, camera.Id.Value, new { name = "Dock 2" });

        response.Status.ShouldBe(204);
        camera.Name.Value.ShouldBe("Dock 2");
        camera.Url.Value.ShouldBe(MisreadAddress);
    }

    [Fact]
    public async Task A_camera_stored_at_a_now_refused_address_can_be_corrected_to_a_valid_one()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, RtspUrl.FromStored(MisreadAddress));

        var response = await SendPatch(host, camera.Id.Value, new { rtspUrl = "rtsp://cam/s" });

        response.Status.ShouldBe(204);
        camera.Url.Value.ShouldBe("rtsp://cam/s");
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Re_sending_the_stored_now_refused_address_is_refused_like_any_other_write()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, RtspUrl.FromStored(MisreadAddress));

        var response = await SendPatch(host, camera.Id.Value, new { rtspUrl = MisreadAddress });

        AssertRefusedWithout(response, "4242");
        camera.Version.Value.ShouldBe(0);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertRefusedWithout(InProcessResponse response, string secret)
    {
        response.Status.ShouldBe(400);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
        response.Body.ShouldNotContain(secret, Case.Insensitive);
        response.Body.ShouldNotContain("admin", Case.Insensitive);
    }

    private static ClaimsPrincipal Caller() => CameraCatalogEndpointHost.Operator(Subject, "dresden");

    private static Task<InProcessResponse> PostRegistration(CameraCatalogEndpointHost host, string address, string? key) =>
        host.SendAsync(
            "POST",
            Register,
            Caller(),
            new InProcessRequest(
                BodyJson: JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions),
                Headers: key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key }));

    private static Camera Existing(CameraCatalogEndpointHost host, RtspUrl url)
    {
        var camera = Camera.Register(
            FabIdentifier.From("dresden"),
            CameraName.From("Dock 1"),
            url,
            OperatorIdentifier.From(Subject),
            host.Clock.Object);
        host.Cameras
            .Setup(cameras => cameras.GetWithinFabAsync(
                FabIdentifier.From("dresden"), camera.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Camera>.Some(camera));

        return camera;
    }

    private static Task<InProcessResponse> SendPatch(CameraCatalogEndpointHost host, Guid identifier, object body) =>
        host.SendAsync(
            "PATCH",
            Patch,
            Caller(),
            new InProcessRequest(
                Route: new Dictionary<string, string> { ["camera"] = identifier.ToString() },
                BodyJson: JsonSerializer.Serialize(body, WebOptions),
                Headers: new Dictionary<string, string> { ["If-Match"] = "\"0\"" }));
}
