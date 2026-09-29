using System.Text.Json;
using System.Text.Json.Serialization;
using SmartSentinelEye.ScenarioSimulator.Cues;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Mqtt;

/// <summary>
/// Maps a sensor sample or a clip cue to the EventIngestion MQTT wire shape
/// (spec 006, spec 289 FR-002): topic <c>fab/munich/{source}/{deviceId}</c> +
/// body <c>{ eventId, kind, occurredAt, payload:{...} }</c>. The deviceId is
/// the asset's camera path so a seeded rule's <c>$.device</c> matches.
/// </summary>
public sealed class MqttSampleMapper(TimeProvider clock)
{
    private const string FabId = "munich";

    // WhenWritingNull is shared by both payload shapes: the sensor payload has
    // no nullable fields, so it is a no-op there, and it is what makes FR-002's
    // "absent optional fields are omitted, not null" true for a detection cue.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MqttSample Map(AssetDefinition asset, SensorDefinition sensor, double value)
    {
        string topic = $"fab/{FabId}/{sensor.Source}/{asset.Camera.Path}";
        string body = JsonSerializer.Serialize(
            new MqttBody<MqttPayload>(
                Guid.CreateVersion7(),
                sensor.Kind,
                clock.GetUtcNow(),
                new MqttPayload(value, sensor.Unit, asset.Key)),
            JsonOpts);
        return new MqttSample(topic, body);
    }

    /// <summary>
    /// Maps a clip cue (spec 289 FR-001/FR-002) to the same wire shape a
    /// sensor sample uses, so a reaction's AEL predicate reads
    /// <c>$.payload.*</c> the same way regardless of which produced the
    /// event. <paramref name="clipOffsetMs"/> is a verification aid, not
    /// part of the detection itself — it lets Phase 5 join an ingested
    /// event back to the cue that produced it without a stopwatch.
    /// </summary>
    public MqttSample Map(AssetDefinition asset, CueDefinition cue, int clipOffsetMs)
    {
        string topic = $"fab/{FabId}/{cue.Source}/{asset.Camera.Path}";
        DetectionBox? box = cue.Box is { } cueBox ? new DetectionBox(cueBox.X, cueBox.Y, cueBox.Width, cueBox.Height) : null;
        string body = JsonSerializer.Serialize(
            new MqttBody<DetectionPayload>(
                Guid.CreateVersion7(),
                cue.Kind,
                clock.GetUtcNow(),
                new DetectionPayload(
                    cue.Class, cue.Confidence, asset.Key, cue.Label, cue.Zone, box, cue.TrackIdentifier, clipOffsetMs)),
            JsonOpts);
        return new MqttSample(topic, body);
    }

    // Mirrors EventIngestion's MqttIngressPayload { eventId, kind, occurredAt, payload }.
    private sealed record MqttBody<TPayload>(Guid EventId, string Kind, DateTimeOffset OccurredAt, TPayload Payload);

    private sealed record MqttPayload(double Value, string Unit, string Station);

    private sealed record DetectionPayload(
        string Class,
        double Confidence,
        string Station,
        string? Label,
        string? Zone,
        DetectionBox? Box,
        string? TrackIdentifier,
        int ClipOffsetMs);

    private sealed record DetectionBox(double X, double Y, double Width, double Height);
}
