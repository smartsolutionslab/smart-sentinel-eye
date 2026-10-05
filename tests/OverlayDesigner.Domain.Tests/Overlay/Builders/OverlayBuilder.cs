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
    private IReadOnlyList<OverlayElement> _elements = [OverlayElement.TextElement("Production Line 1", 48, NormalizedPosition.From(0.5m, 0.05m), NormalizedSize.From(0.3m, 0.08m), OverlayColor.Default)];
    private OperatorIdentifier _createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private IClock _clock = new TestClock(
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture));

    public OverlayBuilder Named(string name)
    {
        _name = OverlayName.From(name);
        return this;
    }

    public OverlayBuilder WithLabel(OverlayElement element)
    {
        _elements = [element];
        return this;
    }

    public OverlayBuilder WithLabels(IReadOnlyList<OverlayElement> elements)
    {
        _elements = elements;
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
        Domain.Overlay.Overlay.CreateDraft(_name, _elements, _createdBy, _clock);

    public IClock Clock => _clock;

    public OperatorIdentifier Operator => _createdBy;

    public sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
