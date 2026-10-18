using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 408 / issue #3031 item 1, FR-003, ADR-0144), captured green on <c>a73df3f1</c> before
/// <c>From</c> learns to refuse a control character or whitespace anywhere in the address. Both EF value
/// converters run when a row is materialised; each must yield the stored text for every address the column holds
/// today, including the ones the write path is about to start refusing. Counterfactual this file catches: a
/// converter that stops using <c>FromStored</c> fails on every row below with the new refusal.
/// </summary>
public class CameraUrlControlCharacterHydrationCharacterisationTests
{
    public static TheoryData<string> StoredAddresses =>
    [
        "rtsp://10.0.0.66/live\r\nX-Inject: 1",
        "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live",
        "rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s",
        "rtsp://[fe80::1%25\u0000]/x",
        "rtsp://10.0.0.66/live x",
        "rtsp://10.0.0.66/live\t",
        "rtsp://[fe80::1]/x ",
        "rtsp://cam ",
        "RTSP://cam/x ",
        "rtsp://10.0.0.66/live?token=a\tSECRET",
        "rtsp://10.0.0.66/live\u0000SECRET",
        "rtsp://10.0.0.66/li\u007Fve",
        "rtsp://10.0.0.66/live?q\u0085x",
        "rtsp://10.0.0.66/live#f\u000Bx",
        "rtsp://@cam/li\u001Fve",
        "rtsp://[fe80::1]/live\x2028",
        "rtsp://[fe80::1%25eth0\x3000]/x",
        "rtsp://[fe80::1%25e\x00A0th0]/x",
        "rtsp://cam\x00A0/live",
        "rtsp://10.0.\x20000.66/live",
        "rtsp://\x3000cam/live",
    ];

    [Fact]
    public void The_stored_set_holds_exactly_the_rows_the_spec_lists()
    {
        StoredAddresses.Cast<object[]>().Count().ShouldBe(21);
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
