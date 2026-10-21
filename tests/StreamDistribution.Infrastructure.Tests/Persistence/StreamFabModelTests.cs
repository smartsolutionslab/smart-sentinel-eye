using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using StreamAggregate = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Persistence;

/// <summary>
/// Spec 423 (#1467), R3. A stream always has a fab, so the <c>streams.fab</c>
/// column is required, and the committed snapshot must still describe the
/// model (a configuration change without its migration fails the second fact).
/// </summary>
public class StreamFabModelTests
{
    private static StreamDistributionDbContext NewContext() =>
        new(new DbContextOptionsBuilder<StreamDistributionDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options);

    [Fact]
    public void The_stream_fab_column_is_required()
    {
        using var context = NewContext();
        var model = ((IInfrastructure<IServiceProvider>)context).Instance
            .GetService(typeof(IDesignTimeModel)) is IDesignTimeModel designTimeModel
            ? designTimeModel.Model
            : throw new InvalidOperationException("The design-time model is unavailable.");

        var fab = model.FindEntityType(typeof(StreamAggregate))!.FindProperty(nameof(StreamAggregate.Fab))!;

        fab.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void The_committed_model_snapshot_still_describes_the_model()
    {
        using var context = NewContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
