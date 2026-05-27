using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;

/// <summary>
/// Fluent builder for Overlay aggregates in tests (ADR-0054). Sensible
/// defaults; .With...() overrides per scenario. Returns an Overlay
/// whose only revision is a fresh Draft so tests typically Publish first.
/// </summary>
public sealed class OverlayBuilder
{
    private OverlayName _name = OverlayName.From("Line-1 Title");
    private Label _label = Label.From("Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48);
    private OperatorIdentifier _createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private IClock _clock = new TestClock(
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture));

    public OverlayBuilder Named(string name)
    {
        _name = OverlayName.From(name);
        return this;
    }

    public OverlayBuilder WithLabel(Label label)
    {
        _label = label;
        return this;
    }

    public OverlayBuilder CreatedBy(OperatorIdentifier createdBy)
    {
        _createdBy = createdBy;
        return this;
    }

    public OverlayBuilder At(DateTimeOffset moment)
    {
        _clock = new TestClock(moment);
        return this;
    }

    public Domain.Overlay.Overlay Build() =>
        Domain.Overlay.Overlay.CreateDraft(_name, _label, _createdBy, _clock);

    public IClock Clock => _clock;

    public OperatorIdentifier Operator => _createdBy;

    public sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
