using System.Globalization;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera.Builders;

/// <summary>
/// Fluent builder for Camera aggregates in tests (ADR-0054). Sensible
/// defaults; .With...() overrides per scenario.
/// </summary>
public sealed class CameraBuilder
{
    private FabIdentifier fab = FabIdentifier.From("munich");
    private CameraName name = CameraName.From("Cam-Default");
    private RtspUrl url = RtspUrl.From("rtsp://10.0.0.1:554/h264");
    private OperatorIdentifier registeredBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private IClock clock = new TestClock(DateTimeOffset.Parse("2026-05-25T10:00:00Z", CultureInfo.InvariantCulture));

    public CameraBuilder WithName(string name)
    {
        this.name = CameraName.From(name);
        return this;
    }

    public CameraBuilder WithUrl(string url)
    {
        this.url = RtspUrl.From(url);
        return this;
    }

    public CameraBuilder RegisteredBy(OperatorIdentifier operatorIdentifier)
    {
        registeredBy = operatorIdentifier;
        return this;
    }

    public CameraBuilder At(DateTimeOffset moment)
    {
        clock = new TestClock(moment);
        return this;
    }

    public CameraBuilder WithFab(string fab)
    {
        this.fab = FabIdentifier.From(fab);
        return this;
    }

    public Domain.Camera.Camera Build() =>
        Domain.Camera.Camera.Register(fab, name, url, registeredBy, clock);

    private sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
