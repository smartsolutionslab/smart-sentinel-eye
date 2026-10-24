using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 450 / issue #3118, phase 4a RED (new behaviour). A row stored with a userinfo segment must be readable
/// through the real EF value converter, so the read handlers can mask it instead of failing the whole fab's
/// list. The converter reads through <c>RtspUrl.FromStored</c>, which must accept the segment.
/// Synthetic markers only: no row below is a real credential.
/// </summary>
public class CameraUrlUserInfoHydrationTests
{
    public static TheoryData<string> StoredAddresses =>
    [
        "rtsp://u:USERPWMARK@10.0.5.98/x",
        "rtsp://admin:pass@cam/s",
        "rtsp://admin:12345%23@cam/s",
        "rtsp://user@[fd00::5]:8554/x",
    ];

    [Theory]
    [MemberData(nameof(StoredAddresses))]
    public void The_converter_reads_a_stored_row_holding_a_userinfo_segment_back_as_it_was_stored(string stored)
    {
        var converter = UrlProperty().GetValueConverter().ShouldNotBeNull();

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
