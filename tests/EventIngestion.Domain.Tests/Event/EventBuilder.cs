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
    private EventIdentifier _identifier = EventIdentifier.New();
    private FabIdentifier _fab = FabIdentifier.From("munich");
    private Source _source = Source.Plc;
    private DeviceIdentifier _device = DeviceIdentifier.From("station-4");
    private Kind _kind = Kind.From("PlcCycleStart");
    private OccurredAt _occurredAt = OccurredAt.From(
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture));
    private Payload _payload = Payload.From("{\"cycleId\":\"abc\"}");
    private FakeClock _clock = new(
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture));

    public EventBuilder WithIdentifier(EventIdentifier identifier) { _identifier = identifier; return this; }
    public EventBuilder WithFab(string fab) { _fab = FabIdentifier.From(fab); return this; }
    public EventBuilder WithSource(Source source) { _source = source; return this; }
    public EventBuilder WithDevice(string device) { _device = DeviceIdentifier.From(device); return this; }
    public EventBuilder WithKind(string kind) { _kind = Kind.From(kind); return this; }
    public EventBuilder WithOccurredAt(DateTimeOffset occurredAt) { _occurredAt = OccurredAt.From(occurredAt); return this; }
    public EventBuilder WithPayload(string rawJson) { _payload = Payload.From(rawJson); return this; }
    public EventBuilder WithClock(DateTimeOffset now) { _clock = new FakeClock(now); return this; }

    public EventAggregate Build() =>
        EventAggregate.Ingest(_identifier, _fab, _source, _device, _kind, _occurredAt, _payload, _clock);
}
