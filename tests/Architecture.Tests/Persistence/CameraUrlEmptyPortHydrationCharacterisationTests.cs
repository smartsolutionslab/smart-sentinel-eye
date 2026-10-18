using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 414 / issue #3031 item 3, FR-003, ADR-0144), captured green on <c>1cbae37d</c> before
/// <c>From</c> learns to refuse a ':' that names no port. Both EF value converters run when a row is materialised;
/// each must yield the stored text for every address the column holds today, including the ones the write path is
/// about to start refusing. Counterfactual this file catches: a converter that stops using <c>FromStored</c> fails on
/// every row below with the new refusal.
/// </summary>
public class CameraUrlEmptyPortHydrationCharacterisationTests
{
    public static TheoryData<string> StoredAddresses => [.. EmptyPortRows.EmptyPort];

    [Fact]
    public void The_stored_set_holds_exactly_the_rows_the_spec_lists()
    {
        StoredAddresses.Cast<object[]>().Count().ShouldBe(24);
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
