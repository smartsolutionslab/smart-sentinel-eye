using System.Text.Json;
using SmartSentinelEye.ScenarioSimulator.Cues;
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

    /// <summary>
    /// Spec 289 / PR-B, T-B05 (ADR-0144 red). <c>MqttSampleMapper.Map(asset,
    /// cue, offsetMs)</c> does not exist yet — plan.md §3.2's overload
    /// (T-B13). The topic uses the cue's own <c>Source</c> (a detection cue is
    /// <c>inference</c> by default, but the mapper must not hard-code that),
    /// and the payload carries the FR-002 detection fields plus
    /// <c>clipOffsetMs</c> — a verification aid, not part of the detection
    /// itself.
    /// </summary>
    [Fact]
    public void A_detection_cue_maps_to_the_inference_topic_and_the_FR002_payload_shape()
    {
        MqttSampleMapper mapper = new(TimeProvider.System);
        AssetDefinition asset = new()
        {
            Key = "station-4-roughing",
            Camera = new CameraDefinition { Path = "station-4-roughing" },
        };
        CueDefinition cue = new(
            AtMs: 12_000, Class: "person", Confidence: 0.92, Kind: "ObjectDetected", Source: "inference",
            Label: "PERSON IN EXCLUSION ZONE", Zone: "exclusion",
            Box: new CueBox(X: 0.61, Y: 0.40, Width: 0.08, Height: 0.31),
            TrackIdentifier: "p-1");

        MqttSample sample = mapper.Map(asset, cue, clipOffsetMs: 12_000);

        sample.Topic.ShouldBe("fab/munich/inference/station-4-roughing");

        using JsonDocument document = JsonDocument.Parse(sample.Payload);
        JsonElement root = document.RootElement;
        root.GetProperty("kind").GetString().ShouldBe("ObjectDetected");
        root.TryGetProperty("eventId", out _).ShouldBeTrue();
        root.TryGetProperty("occurredAt", out _).ShouldBeTrue();

        JsonElement payload = root.GetProperty("payload");
        payload.GetProperty("class").GetString().ShouldBe("person");
        payload.GetProperty("confidence").GetDouble().ShouldBe(0.92);
        payload.GetProperty("station").GetString().ShouldBe("station-4-roughing");
        payload.GetProperty("label").GetString().ShouldBe("PERSON IN EXCLUSION ZONE");
        payload.GetProperty("zone").GetString().ShouldBe("exclusion");
        payload.GetProperty("trackIdentifier").GetString().ShouldBe("p-1");
        payload.GetProperty("clipOffsetMs").GetInt32().ShouldBe(12_000);

        JsonElement box = payload.GetProperty("box");
        box.GetProperty("x").GetDouble().ShouldBe(0.61);
        box.GetProperty("y").GetDouble().ShouldBe(0.40);
        box.GetProperty("width").GetDouble().ShouldBe(0.08);
        box.GetProperty("height").GetDouble().ShouldBe(0.31);
    }

    /// <summary>
    /// FR-002: "absent optional fields are omitted, not null" — so an AEL
    /// predicate reading a missing field fails evaluation (logged, skipped)
    /// rather than comparing against a JSON null.
    /// </summary>
    [Fact]
    public void Absent_optional_detection_fields_are_omitted_from_the_payload_rather_than_written_as_null()
    {
        MqttSampleMapper mapper = new(TimeProvider.System);
        AssetDefinition asset = new()
        {
            Key = "coiler",
            Camera = new CameraDefinition { Path = "coiler" },
        };
        CueDefinition cue = new(AtMs: 4_000, Class: "person", Confidence: 0.55);

        MqttSample sample = mapper.Map(asset, cue, clipOffsetMs: 4_000);

        using JsonDocument document = JsonDocument.Parse(sample.Payload);
        JsonElement payload = document.RootElement.GetProperty("payload");

        payload.TryGetProperty("label", out _).ShouldBeFalse();
        payload.TryGetProperty("zone", out _).ShouldBeFalse();
        payload.TryGetProperty("box", out _).ShouldBeFalse();
        payload.TryGetProperty("trackIdentifier", out _).ShouldBeFalse();
    }
}
