using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;

/// <summary>
/// Fluent builder for Layout aggregates in tests (ADR-0054). Sensible
/// defaults; .With...() overrides per scenario. Returns a Layout whose
/// only revision is a fresh Draft so tests typically Publish first.
/// </summary>
public sealed class LayoutBuilder
{
    private LayoutName _name = LayoutName.From("Line-1-Entrance");
    private CameraIdentifier _camera = CameraIdentifier.From(Guid.CreateVersion7());
    private OperatorIdentifier _createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private OverlayIdentifier? _overlay;
    private IClock _clock = new TestClock(
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture));

    public LayoutBuilder Named(string name)
    {
        _name = LayoutName.From(name);
        return this;
    }

    public LayoutBuilder ForCamera(CameraIdentifier camera)
    {
        _camera = camera;
        return this;
    }

    public LayoutBuilder WithOverlay(OverlayIdentifier overlay)
    {
        _overlay = overlay;
        return this;
    }

    public LayoutBuilder CreatedBy(OperatorIdentifier createdBy)
    {
        _createdBy = createdBy;
        return this;
    }

    public LayoutBuilder At(DateTimeOffset moment)
    {
        _clock = new TestClock(moment);
        return this;
    }

    public Domain.Layout.Layout Build() =>
        Domain.Layout.Layout.CreateDraft(_name, _camera, _createdBy, _clock, _overlay);

    public IClock Clock => _clock;

    public OperatorIdentifier Operator => _createdBy;

    public sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
