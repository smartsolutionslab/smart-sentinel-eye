using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 389 / issue #2999, ADR-0144), captured green before <see cref="RtspUrl.From"/> learns
/// to refuse a raw '@' after the authority. A row already in <c>cameras.rtsp_url</c> must stay readable: the
/// value converter EF runs when it materialises a camera must yield the stored text for every address the
/// column can hold today, including the ones the write path is about to start refusing. A camera list that
/// throws on one such row takes the whole fab's list down.
///
/// <para>
/// Counterfactual this file catches: tighten <c>RtspUrl.From</c> without switching the converter's read lambda
/// to a non-validating factory, and every row of <see cref="StoredAddresses"/> that the new rule refuses fails
/// here with the refusal.
/// </para>
/// </summary>
public class CameraUrlHydrationCharacterisationTests
{
    public static TheoryData<string> StoredAddresses =>
    [
        "rtsp://10.0.5.12/h264",
        "rtsp://cam:554/path?x=y",
        "RTSP://CAM/s",
        "rtsp://[::1]:554/s",
        "rtsp://[fe80::1]/s",
        "rtsp://cam/s;x",
        "rtsp://cam/%40x",
        "rtsp://cam/s?mail=a%40b",
        "rtsp://admin:12345#@cam/s",
        "rtsp://admin:4711?@cam/s",
        "rtsp://admin:8080/x@cam/s",
        "RTSP://ADMIN:12345#@CAM",
        "rtsp://3232235777:12345#@cam",
        "rtsp://12345#@cam/s",
        "rtsp://admin:12345#@[fd00::5]:8554/x",
        "rtsp://cam/s?mail=a@b",
        "rtsp://cam/a@b",
        "rtsp://cam/s#frag@x",
    ];

    [Theory]
    [MemberData(nameof(StoredAddresses))]
    public void The_converter_reads_a_stored_address_back_as_it_was_stored(string stored)
    {
        var converter = UrlProperty().GetValueConverter();

        converter.ShouldNotBeNull();
        var hydrated = converter.ConvertFromProvider(stored).ShouldBeOfType<RtspUrl>();
        hydrated.Value.ShouldBe(stored);
    }

    private static IProperty UrlProperty()
    {
        var options = new DbContextOptionsBuilder<CameraCatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using CameraCatalogDbContext context = new(options);
        var model = ((IInfrastructure<IServiceProvider>)context).Instance
            .GetService(typeof(IDesignTimeModel)) is IDesignTimeModel designTimeModel
            ? designTimeModel.Model
            : throw new InvalidOperationException("The design-time model is unavailable.");

        return model.FindEntityType(typeof(Camera))!.FindProperty(nameof(Camera.Url))!;
    }
}
