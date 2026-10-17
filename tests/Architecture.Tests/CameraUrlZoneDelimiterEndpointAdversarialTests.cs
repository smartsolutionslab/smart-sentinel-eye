using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 394 / issue #3023 items 1, 2 and 5, adversarial, on the in-process CameraCatalog host. Variants beyond the
/// written rows reach <c>POST</c> and <c>PATCH /cameras</c> as a 400 with no part of the address in the body, never
/// as a 500: unreadable hosts behind a query, a fragment, a port or a zone, userinfo hiding an unreadable host, a
/// delimiter beside whitespace or a raw '%' zone. A keyed retry of a registration whose address is now refused
/// answers 400 before the idempotency store is consulted, so it never replays the original 201 (spec 389 A-3,
/// extended to these shapes). A camera stored at such an address (read back through <c>FromStored</c>) can still be
/// renamed and corrected; re-sending its own stored address is refused like any other write.
/// </summary>
public class CameraUrlZoneDelimiterEndpointAdversarialTests
{
    private const string Register = "/cameras/";

    private const string Patch = "/cameras/{camera:guid}";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000003");

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string> Variants => new()
    {
        { "rtsp://[1:2:3:4:5:6:7:8:/64]?x", "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]#x", "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]:554/s", "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:%25eth0/64]/s", "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/0]x/s", "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]@evil.com/s", "evil.com" },
        { "rtsp://[x]@[1:2:3:4:5:6:7:8:/64]x/s", "1:2:3:4" },
        { "rtsp://:@[1:2:3:4:5:6:7:8:/64]/s", "1:2:3:4" },
        { "rtsp://[fe80::1%25 ?evil]/s", "evil" },
        { "rtsp://[fe80::1%?evil.com]/s", "evil.com" },
        { "RTSP://@[FE80::1%25ETH0@10.0.0.66]/s", "10.0.0.66" },
        { "rtsp://[fe80::1%25eth0#]", "eth0#" },
    };

    public static TheoryData<string> StoredLegacyAddresses =>
    [
        "rtsp://[1:2:3:4:5:6:7:8:/64]",
        "rtsp://[1:2:3:4:5:6:7:8:/64]/live",
        "rtsp://@[fe80::1%@10.0.0.66:8554]/live",
        "rtsp://[::1%25x?y]/s",
    ];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task Registering_a_variant_is_a_400_and_the_body_carries_none_of_it(string address, string secret)
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, "POST", Register, Registration(address, key: null));

        AssertRefusedWithout(response, secret);
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task Changing_an_address_to_a_variant_is_a_400_and_the_camera_keeps_its_address(string address, string secret)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, RtspUrl.From("rtsp://10.0.0.5/stream"));

        var response = await Send(host, "PATCH", Patch, PatchOf(camera, new { rtspUrl = address }));

        AssertRefusedWithout(response, secret);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("rtsp://@[fe80::1%@10.0.0.66:8554]/live", "10.0.0.66")]
    [InlineData("rtsp://[1:2:3:4:5:6:7:8:/64]", "1:2:3:4")]
    public async Task A_keyed_retry_of_a_now_refused_registration_answers_400_without_consulting_the_store(
        string address,
        string secret)
    {
        await using var host = new CameraCatalogEndpointHost();
        host.IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyReservation.CompletedWith(new Guid("0197a1b2-0000-7000-8000-0000000000ab")));

        var response = await Send(host, "POST", Register, Registration(address, key: "retry-after-deploy"));

        AssertRefusedWithout(response, secret);
        response.Location.ShouldBeEmpty();
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(StoredLegacyAddresses))]
    public async Task A_camera_stored_at_a_now_refused_address_can_still_be_renamed(string stored)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, RtspUrl.FromStored(stored));

        var response = await Send(host, "PATCH", Patch, PatchOf(camera, new { name = "Dock 2" }));

        response.Status.ShouldBe(204, response.Body);
        camera.Name.Value.ShouldBe("Dock 2");
        camera.Url.Value.ShouldBe(stored);
    }

    [Theory]
    [MemberData(nameof(StoredLegacyAddresses))]
    public async Task A_camera_stored_at_a_now_refused_address_can_be_corrected_to_a_valid_one(string stored)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, RtspUrl.FromStored(stored));

        var response = await Send(host, "PATCH", Patch, PatchOf(camera, new { rtspUrl = "rtsp://cam/s" }));

        response.Status.ShouldBe(204, response.Body);
        camera.Url.Value.ShouldBe("rtsp://cam/s");
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(StoredLegacyAddresses))]
    public async Task Re_sending_the_stored_now_refused_address_is_refused_like_any_other_write(string stored)
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host, RtspUrl.FromStored(stored));

        var response = await Send(host, "PATCH", Patch, PatchOf(camera, new { rtspUrl = stored }));

        response.Status.ShouldBe(400, response.Body);
        camera.Version.Value.ShouldBe(0);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertRefusedWithout(InProcessResponse response, string secret)
    {
        response.Status.ShouldBe(400, response.Body);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("CAMERA_INVALID_REQUEST");
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

    private static InProcessRequest Registration(string address, string? key) =>
        new(
            BodyJson: JsonSerializer.Serialize(new RegisterCameraRequest { Name = "Dock 1", RtspUrl = address }, WebOptions),
            Headers: key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key });

    private static InProcessRequest PatchOf(Camera camera, object body) =>
        new(
            Route: new Dictionary<string, string> { ["camera"] = camera.Id.Value.ToString() },
            BodyJson: JsonSerializer.Serialize(body, WebOptions),
            Headers: new Dictionary<string, string> { ["If-Match"] = "\"0\"" });

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
}
