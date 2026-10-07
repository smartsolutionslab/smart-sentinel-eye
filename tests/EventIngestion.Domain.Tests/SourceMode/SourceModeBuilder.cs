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
    private FabIdentifier fab = FabIdentifier.From("dresden");
    private Source source = Source.Manual;
    private Domain.SourceMode.EventTypeMode mode = Domain.SourceMode.EventTypeMode.Strict;
    private OperatorIdentifier declaredBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock clock = new(
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture));

    public SourceModeBuilder WithFab(string fab) { this.fab = FabIdentifier.From(fab); return this; }
    public SourceModeBuilder WithSource(Source source) { this.source = source; return this; }
    public SourceModeBuilder WithMode(Domain.SourceMode.EventTypeMode mode) { this.mode = mode; return this; }
    public SourceModeBuilder DeclaredBy(OperatorIdentifier declaredBy) { this.declaredBy = declaredBy; return this; }
    public SourceModeBuilder At(DateTimeOffset moment) { clock = new FakeClock(moment); return this; }

    public Domain.SourceMode.SourceMode Build() =>
        Domain.SourceMode.SourceMode.Declare(fab, source, mode, declaredBy, clock);
}
