namespace SmartSentinelEye.ScenarioSimulator.Mqtt;

/// <summary>A ready-to-publish MQTT sample: the topic + the serialized JSON body.</summary>
public sealed record MqttSample(string Topic, string Payload);
