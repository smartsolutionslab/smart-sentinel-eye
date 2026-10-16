using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

/// <summary>
/// Spec 381 / issue #2993. An address whose userinfo the parser read as host and port is logged as the
/// placeholder; the stored address stays complete.
/// </summary>
public class ChangeCameraAddressUserInfoLogRedactionTests
{
    private const string Credential = "operator:2468";
    private const string CutShortAddress = "rtsp://operator:2468#!@10.0.0.5:554/h264";

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-10-16T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Changing_the_address_to_a_cut_short_userinfo_logs_the_placeholder()
    {
        (var handler, var cameras, var logger) = NewHandler();
        var camera = await SeedAsync(cameras);

        var result = await handler.HandleAsync(CommandFor(camera, CutShortAddress), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        logger.Entries.Select(entry => entry.Message).ShouldContain(
            $"Changed camera {camera.Id} address to <unparseable url>.");
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(Credential, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Changing_the_address_to_a_cut_short_userinfo_still_stores_the_full_url()
    {
        (var handler, var cameras, var _) = NewHandler();
        var camera = await SeedAsync(cameras);

        await handler.HandleAsync(CommandFor(camera, CutShortAddress), CancellationToken.None);

        cameras.Cameras.Single().Url.Value.ShouldBe(CutShortAddress);
    }

    private static ChangeCameraAddressCommand CommandFor(Camera camera, string url) =>
        new(FabIdentifier.From("munich"), camera.Id, RtspUrl.From(url), camera.Version, AnAdmin);

    private static async Task<Camera> SeedAsync(InMemoryCameraRepository cameras)
    {
        var camera = Camera.Register(
            FabIdentifier.From("munich"),
            CameraName.From("line-3-inlet"),
            RtspUrl.From("rtsp://10.0.5.12/h264"),
            AnAdmin,
            new FixedClock(FixedMoment));

        cameras.Add(camera);
        await cameras.SaveAsync(CancellationToken.None);
        camera.ClearPendingEvents();

        return camera;
    }

    private static (
        ChangeCameraAddressCommandHandler Handler,
        InMemoryCameraRepository Cameras,
        CapturingLogger<ChangeCameraAddressCommandHandler> Logger) NewHandler()
    {
        InMemoryCameraRepository cameras = new();
        CapturingLogger<ChangeCameraAddressCommandHandler> logger = new();

        return (new ChangeCameraAddressCommandHandler(cameras, new FixedClock(FixedMoment), logger), cameras, logger);
    }
}
