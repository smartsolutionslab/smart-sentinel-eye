using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.RegisteredEventType;

/// <summary>
/// Hand-written fluent builder for <see cref="Domain.RegisteredEventType.RegisteredEventType"/>
/// aggregates per ADR-0054. Sensible defaults so tests can override only the
/// fields they care about.
/// </summary>
public sealed class RegisteredEventTypeBuilder
{
    private FabIdentifier fab = FabIdentifier.From("dresden");
    private Kind kind = Kind.From("PersonInRestrictedZone");
    private OperatorIdentifier registeredBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock clock = new(
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture));

    public RegisteredEventTypeBuilder WithFab(string fab) { this.fab = FabIdentifier.From(fab); return this; }
    public RegisteredEventTypeBuilder WithKind(string kind) { this.kind = Kind.From(kind); return this; }
    public RegisteredEventTypeBuilder RegisteredBy(OperatorIdentifier operatorIdentifier) { registeredBy = operatorIdentifier; return this; }
    public RegisteredEventTypeBuilder At(DateTimeOffset moment) { clock = new FakeClock(moment); return this; }

    public Domain.RegisteredEventType.RegisteredEventType Build() =>
        Domain.RegisteredEventType.RegisteredEventType.Register(fab, kind, registeredBy, clock);
}
