using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

/// <summary>
/// Spec 381 / issue #2993, changed by spec 389 / issue #2999. An address whose userinfo the parser would read as
/// host and port used to be accepted, logged as the placeholder and stored in full. It is now refused where the
/// value object is built, so it never reaches the handler: the refusal carries the fix text and no part of the
/// address, and the camera keeps the address it had.
/// </summary>
public class ChangeCameraAddressUserInfoLogRedactionTests
{
    private const string Credential = "operator:2468";
    private const string CutShortAddress = "rtsp://operator:2468#!@10.0.0.5:554/h264";
    private const string OriginalAddress = "rtsp://10.0.5.12/h264";

    private const string Refusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-10-16T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public void Changing_the_address_to_a_cut_short_userinfo_is_refused_without_echoing_the_credential()
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(CutShortAddress));

        refused.Message.ShouldBe(Refusal);
        refused.Message.ShouldNotContain(Credential, Case.Insensitive);
    }

    [Fact]
    public async Task Changing_the_address_to_a_cut_short_userinfo_leaves_the_stored_address_unchanged()
    {
        InMemoryCameraRepository cameras = new();
        var camera = await SeedAsync(cameras);

        Should.Throw<ArgumentException>(() => CommandFor(camera, CutShortAddress));

        cameras.Cameras.Single().Url.Value.ShouldBe(OriginalAddress);
    }

    private static ChangeCameraAddressCommand CommandFor(Camera camera, string url) =>
        new(FabIdentifier.From("munich"), camera.Id, RtspUrl.From(url), camera.Version, AnAdmin);

    private static async Task<Camera> SeedAsync(InMemoryCameraRepository cameras)
    {
        var camera = Camera.Register(
            FabIdentifier.From("munich"),
            CameraName.From("line-3-inlet"),
            RtspUrl.From(OriginalAddress),
            AnAdmin,
            new FixedClock(FixedMoment));

        cameras.Add(camera);
        await cameras.SaveAsync(CancellationToken.None);
        camera.ClearPendingEvents();

        return camera;
    }
}
