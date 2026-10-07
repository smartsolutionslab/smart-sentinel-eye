using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

/// <summary>
/// Hand-written fluent builder for <see cref="Event"/> aggregates
/// per ADR-0054. Sensible defaults so tests can override only the
/// fields they care about.
/// </summary>
public sealed class EventBuilder
{
    private EventIdentifier identifier = EventIdentifier.New();
    private FabIdentifier fab = FabIdentifier.From("munich");
    private Source source = Source.Plc;
    private DeviceIdentifier device = DeviceIdentifier.From("station-4");
    private Kind kind = Kind.From("PlcCycleStart");
    private OccurredAt occurredAt = OccurredAt.From(
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture));
    private Payload payload = Payload.From("{\"cycleId\":\"abc\"}");
    private FakeClock clock = new(
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture));

    public EventBuilder WithIdentifier(EventIdentifier identifier) { this.identifier = identifier; return this; }
    public EventBuilder WithFab(string fab) { this.fab = FabIdentifier.From(fab); return this; }
    public EventBuilder WithSource(Source source) { this.source = source; return this; }
    public EventBuilder WithDevice(string device) { this.device = DeviceIdentifier.From(device); return this; }
    public EventBuilder WithKind(string kind) { this.kind = Kind.From(kind); return this; }
    public EventBuilder WithOccurredAt(DateTimeOffset occurredAt) { this.occurredAt = OccurredAt.From(occurredAt); return this; }
    public EventBuilder WithPayload(string rawJson) { payload = Payload.From(rawJson); return this; }
    public EventBuilder WithClock(DateTimeOffset now) { clock = new FakeClock(now); return this; }

    public EventAggregate Build() =>
        EventAggregate.Ingest(identifier, fab, source, device, kind, occurredAt, payload, clock);
}
