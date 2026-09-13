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
    private FabIdentifier _fab = FabIdentifier.From("dresden");
    private Kind _kind = Kind.From("PersonInRestrictedZone");
    private OperatorIdentifier _registeredBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock _clock = new(
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture));

    public RegisteredEventTypeBuilder WithFab(string fab) { _fab = FabIdentifier.From(fab); return this; }
    public RegisteredEventTypeBuilder WithKind(string kind) { _kind = Kind.From(kind); return this; }
    public RegisteredEventTypeBuilder RegisteredBy(OperatorIdentifier operatorIdentifier) { _registeredBy = operatorIdentifier; return this; }
    public RegisteredEventTypeBuilder At(DateTimeOffset moment) { _clock = new FakeClock(moment); return this; }

    public Domain.RegisteredEventType.RegisteredEventType Build() =>
        Domain.RegisteredEventType.RegisteredEventType.Register(_fab, _kind, _registeredBy, _clock);
}
