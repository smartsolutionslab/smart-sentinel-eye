using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 392 / issues #3012 and #3017, US2a, ADR-0144), captured green on <c>de9802b1</c> before
/// <c>From</c> learns to refuse an empty host and text after the closing bracket. Both EF value converters run
/// when a row is materialised; each must yield the stored text for every address the column holds today,
/// including the ones the write path is about to start refusing. Counterfactual this file catches: a converter
/// that stops using <c>FromStored</c> fails on every row below with the new refusal.
/// </summary>
public class CameraUrlAuthorityHostHydrationCharacterisationTests
{
    public static TheoryData<string> StoredAddresses =>
    [
        "rtsp://",
        "rtsp:///",
        "rtsp:///cam",
        "RTSP:///cam",
        "rtsp:///admin:4242@cam/s",
        "rtsp:////admin:4242@cam/s",
        "rtsp://?x",
        "rtsp://#x",
        "rtsp:///?x",
        "rtsp://[fe80::1]%25eth0/x",
        "rtsp://[fe80::1]%eth0/x",
        "rtsp://[fe80::1]garbage.example/x",
        "rtsp://[fe80::1]garbage.example",
        "rtsp://[fe80::1]x:554/s",
        "rtsp://[fe80::1]]/x",
        "RTSP://[FE80::1]GARBAGE/x",
        "rtsp://[::ffff:10.0.0.1]x/s",
        "rtsp://[fe80::1/64]x/s",
        "rtsp://[fe80::1]%20/s",
    ];

    [Theory]
    [MemberData(nameof(StoredAddresses))]
    public void The_camera_converter_reads_a_stored_address_back_as_it_was_stored(string stored)
    {
        var converter = CameraUrlProperty().GetValueConverter().ShouldNotBeNull();

        var hydrated = converter.ConvertFromProvider(stored).ShouldBeOfType<RtspUrl>();
        hydrated.Value.ShouldBe(stored);
    }

    [Theory]
    [MemberData(nameof(StoredAddresses))]
    public void The_stream_converter_reads_a_stored_address_back_as_it_was_stored(string stored)
    {
        var converter = StreamSourceUrlProperty().GetValueConverter().ShouldNotBeNull();

        var hydrated = converter.ConvertFromProvider(stored).ShouldBeOfType<StreamSourceUrl>();
        hydrated.Value.ShouldBe(stored);
    }

    private static IProperty CameraUrlProperty()
    {
        var options = new DbContextOptionsBuilder<CameraCatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using CameraCatalogDbContext context = new(options);

        return DesignTimeModel(context).FindEntityType(typeof(Camera))!.FindProperty(nameof(Camera.Url))!;
    }

    private static IProperty StreamSourceUrlProperty()
    {
        var options = new DbContextOptionsBuilder<StreamDistributionDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using StreamDistributionDbContext context = new(options);

        return DesignTimeModel(context)
            .FindEntityType(typeof(StreamDistribution.Domain.Stream.Stream))!
            .FindProperty(nameof(StreamDistribution.Domain.Stream.Stream.SourceUrl))!;
    }

    private static IModel DesignTimeModel(DbContext context) =>
        ((IInfrastructure<IServiceProvider>)context).Instance.GetService(typeof(IDesignTimeModel)) is IDesignTimeModel designTimeModel
            ? designTimeModel.Model
            : throw new InvalidOperationException("The design-time model is unavailable.");
}
