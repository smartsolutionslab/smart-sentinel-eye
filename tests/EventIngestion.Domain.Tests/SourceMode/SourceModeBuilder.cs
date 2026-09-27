using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.SourceMode;

/// <summary>
/// Hand-written fluent builder for <see cref="Domain.SourceMode.SourceMode"/>
/// aggregates per ADR-0054, mirroring
/// <c>RegisteredEventType/RegisteredEventTypeBuilder.cs</c>. Sensible defaults
/// so tests can override only the fields they care about.
/// </summary>
public sealed class SourceModeBuilder
{
    private FabIdentifier _fab = FabIdentifier.From("dresden");
    private Source _source = Source.Manual;
    private Domain.SourceMode.EventTypeMode _mode = Domain.SourceMode.EventTypeMode.Strict;
    private OperatorIdentifier _declaredBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock _clock = new(
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture));

    public SourceModeBuilder WithFab(string fab) { _fab = FabIdentifier.From(fab); return this; }
    public SourceModeBuilder WithSource(Source source) { _source = source; return this; }
    public SourceModeBuilder WithMode(Domain.SourceMode.EventTypeMode mode) { _mode = mode; return this; }
    public SourceModeBuilder DeclaredBy(OperatorIdentifier declaredBy) { _declaredBy = declaredBy; return this; }
    public SourceModeBuilder At(DateTimeOffset moment) { _clock = new FakeClock(moment); return this; }

    public Domain.SourceMode.SourceMode Build() =>
        Domain.SourceMode.SourceMode.Declare(_fab, _source, _mode, _declaredBy, _clock);
}
