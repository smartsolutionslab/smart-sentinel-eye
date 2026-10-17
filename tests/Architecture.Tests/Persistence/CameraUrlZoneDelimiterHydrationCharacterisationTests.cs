using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 394 / issue #3023, US2a, ADR-0144), captured green on <c>fbd4176b</c> before
/// <c>From</c> learns to refuse '@', '?' and '#' inside the IPv6 brackets and an unreadable host. Both EF value
/// converters run when a row is materialised; each must yield the stored text for every address the column holds
/// today, including the ones the write path is about to start refusing. Counterfactual this file catches: a
/// converter that stops using <c>FromStored</c> fails on every row below with the new refusal.
/// </summary>
public class CameraUrlZoneDelimiterHydrationCharacterisationTests
{
    public static TheoryData<string> StoredAddresses =>
    [
        "rtsp://@[fe80::1%@10.0.0.66:8554]/live",
        "rtsp://@[::1%25@evil.com:554]/s",
        "rtsp://@[fe80::1%25eth0@]/s",
        "rtsp://[::1%25x?y]/s",
        "rtsp://[::1%25x#y]/s",
        "rtsp://[fe80::1%25a?b]/x",
        "rtsp://[fe80::1%x?y]/s",
        "rtsp://[::1%25x?y]",
        "rtsp://[fe80::1%25eth0#]:554/s",
        "rtsp://@[::1%25x?y]/s",
        "rtsp://[1:2:3:4:5:6:7:8:/64]",
        "rtsp://[::1:2:3:4:5:6:7:/64]/s",
        "rtsp://[1:2:3:4:5:6:7:8:/64]/live",
        "rtsp://[1:2:3:4:5:6:7:8:/64]x/s",
        "RTSP://@[1:2:3:4:5:6:7:8:/64%25eth0]",
    ];

    [Fact]
    public void The_stored_set_holds_exactly_the_rows_the_spec_lists()
    {
        StoredAddresses.Cast<object[]>().Count().ShouldBe(15);
    }

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
