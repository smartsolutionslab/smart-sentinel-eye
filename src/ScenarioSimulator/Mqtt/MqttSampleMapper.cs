using System.Text.Json;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Mqtt;

/// <summary>
/// Maps a sensor sample to the EventIngestion MQTT wire shape (spec 006): topic
/// <c>fab/munich/{source}/{deviceId}</c> + body
/// <c>{ eventId, kind, occurredAt, payload:{ value, unit, station } }</c>. The
/// deviceId is the asset's camera path so the seeded rule's <c>$.device</c>
/// matches, and <c>$.payload.value</c> is what the threshold compares.
/// </summary>
public sealed class MqttSampleMapper(TimeProvider clock)
{
    private const string FabId = "munich";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public MqttSample Map(AssetDefinition asset, SensorDefinition sensor, double value)
    {
        string topic = $"fab/{FabId}/{sensor.Source}/{asset.Camera.Path}";
        string body = JsonSerializer.Serialize(
            new MqttBody(
                Guid.CreateVersion7(),
                sensor.Kind,
                clock.GetUtcNow(),
                new MqttPayload(value, sensor.Unit, asset.Key)),
            JsonOpts);
        return new MqttSample(topic, body);
    }

    // Mirrors EventIngestion's MqttIngressPayload { eventId, kind, occurredAt, payload }.
    private sealed record MqttBody(Guid EventId, string Kind, DateTimeOffset OccurredAt, MqttPayload Payload);

    private sealed record MqttPayload(double Value, string Unit, string Station);
}
