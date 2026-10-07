using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

/// <summary>
/// Fluent builder for Stream aggregates in tests (ADR-0054). Sensible
/// defaults; .With...() overrides per scenario.
/// </summary>
public sealed class StreamBuilder
{
    private FabIdentifier fab = FabIdentifier.From("munich");
    private CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
    private StreamSourceUrl sourceUrl = StreamSourceUrl.From("rtsp://camera-sim:8554/default");
    private OperatorIdentifier provisionedBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private IClock clock = new TestClock(DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture));

    public StreamBuilder WithFab(FabIdentifier fab)
    {
        this.fab = fab;
        return this;
    }

    public StreamBuilder ForCamera(CameraIdentifier camera)
    {
        this.camera = camera;
        return this;
    }

    public StreamBuilder WithSourceUrl(StreamSourceUrl sourceUrl)
    {
        this.sourceUrl = sourceUrl;
        return this;
    }

    public StreamBuilder ProvisionedBy(OperatorIdentifier operatorIdentifier)
    {
        provisionedBy = operatorIdentifier;
        return this;
    }

    public StreamBuilder At(DateTimeOffset moment)
    {
        clock = new TestClock(moment);
        return this;
    }

    public Domain.Stream.Stream Build() =>
        Domain.Stream.Stream.Provision(fab, camera, sourceUrl, provisionedBy, clock);

    private sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
