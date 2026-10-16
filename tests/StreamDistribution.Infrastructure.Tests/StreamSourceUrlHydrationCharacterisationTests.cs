using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 389 / issue #2999, ADR-0144), captured green before <see cref="StreamSourceUrl.From"/>
/// learns to refuse a raw '@' after the authority. A stream row already stored with such a source must stay
/// readable: the value converter EF runs when it materialises a stream must yield the stored text for every
/// source the column can hold today. The MediaMTX reconciler reads every stream at startup, so a row that throws
/// on read fails its whole pass.
///
/// <para>
/// Counterfactual this file catches: tighten <c>StreamSourceUrl.From</c> without switching the converter's read
/// lambda to a non-validating factory, and every row of <see cref="StoredSources"/> that the new rule refuses
/// fails here with the refusal.
/// </para>
/// </summary>
public class StreamSourceUrlHydrationCharacterisationTests
{
    public static TheoryData<string> StoredSources =>
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
    [MemberData(nameof(StoredSources))]
    public void The_converter_reads_a_stored_source_back_as_it_was_stored(string stored)
    {
        var converter = SourceUrlProperty().GetValueConverter();

        converter.ShouldNotBeNull();
        var hydrated = converter.ConvertFromProvider(stored).ShouldBeOfType<StreamSourceUrl>();
        hydrated.Value.ShouldBe(stored);
    }

    private static IProperty SourceUrlProperty()
    {
        var options = new DbContextOptionsBuilder<StreamDistributionDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using StreamDistributionDbContext context = new(options);
        var model = ((IInfrastructure<IServiceProvider>)context).Instance
            .GetService(typeof(IDesignTimeModel)) is IDesignTimeModel designTimeModel
            ? designTimeModel.Model
            : throw new InvalidOperationException("The design-time model is unavailable.");

        return model.FindEntityType(typeof(Domain.Stream.Stream))!.FindProperty(nameof(Domain.Stream.Stream.SourceUrl))!;
    }
}
