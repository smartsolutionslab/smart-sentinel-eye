using System.Text.Json;
using SmartSentinelEye.ScenarioSimulator.Mqtt;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

public class MqttSampleMapperTests
{
    [Fact]
    public void Maps_to_the_per_device_topic_and_the_eventingestion_payload_shape()
    {
        MqttSampleMapper mapper = new(TimeProvider.System);
        AssetDefinition asset = new()
        {
            Key = "station-4-roughing",
            Camera = new CameraDefinition { Path = "station-4-roughing" },
        };
        SensorDefinition sensor = new() { Kind = "temperature", Unit = "degC", Source = "plc" };

        MqttSample sample = mapper.Map(asset, sensor, 1180d);

        sample.Topic.ShouldBe("fab/munich/plc/station-4-roughing");

        using JsonDocument document = JsonDocument.Parse(sample.Payload);
        JsonElement root = document.RootElement;
        root.GetProperty("kind").GetString().ShouldBe("temperature");
        root.TryGetProperty("eventId", out _).ShouldBeTrue();
        root.TryGetProperty("occurredAt", out _).ShouldBeTrue();

        JsonElement payload = root.GetProperty("payload");
        payload.GetProperty("value").GetDouble().ShouldBe(1180d);
        payload.GetProperty("unit").GetString().ShouldBe("degC");
        payload.GetProperty("station").GetString().ShouldBe("station-4-roughing");
    }

    [Fact]
    public void Inference_sensors_route_to_the_inference_topic_segment()
    {
        MqttSampleMapper mapper = new(TimeProvider.System);
        AssetDefinition asset = new()
        {
            Key = "coiler",
            Camera = new CameraDefinition { Path = "coiler" },
        };
        SensorDefinition sensor = new() { Kind = "coil-weight", Unit = "t", Source = "inference" };

        mapper.Map(asset, sensor, 24d).Topic.ShouldBe("fab/munich/inference/coiler");
    }
}
