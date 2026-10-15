using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

/// <summary>
/// Spec 373 / issue #2908. The address-change log line names the new address
/// by origin only; the stored address stays complete (FR-005).
/// </summary>
public class ChangeCameraAddressLogRedactionTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-10-15T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Theory]
    [InlineData("rtsp://10.0.5.44/stream?token=SECRET")]
    [InlineData("rtsp://10.0.5.44/SECRET/path")]
    public async Task Changing_the_address_logs_the_new_origin_only(string newUrl)
    {
        (var handler, var cameras, var logger) = NewHandler();
        var camera = await SeedAsync(cameras);

        var result = await handler.HandleAsync(CommandFor(camera, newUrl), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        logger.Entries.Select(entry => entry.Message).ShouldContain(
            $"Changed camera {camera.Id} address to rtsp://10.0.5.44.");
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("SECRET", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("rtsp://10.0.5.44/stream?token=SECRET")]
    [InlineData("rtsp://10.0.5.44/SECRET/path")]
    public async Task Changing_the_address_still_stores_the_full_url(string newUrl)
    {
        (var handler, var cameras, var _) = NewHandler();
        var camera = await SeedAsync(cameras);

        await handler.HandleAsync(CommandFor(camera, newUrl), CancellationToken.None);

        cameras.Cameras.Single().Url.Value.ShouldBe(newUrl);
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
