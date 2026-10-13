using System.Globalization;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 346, behaviour-preserving): pins the exact text a
/// refused MQTT delivery is dead-lettered with, which refusal wins when a
/// delivery is wrong twice, and that a well-formed delivery maps every
/// field to its own slot. <see cref="MqttEnvelopeParsingTests"/> only asserts
/// that a refusal happened. These must pass unmodified after
/// <c>TryParseEnvelope</c> is split.
/// </summary>
public class MqttEnvelopeRefusalCharacterisationTests
{
    private const string GoodTopic = "fab/munich/plc/dev-1";
    private const string GoodEventId = "01977f5a-6e3d-7c3a-8f1a-0000000000a1";

    [Theory]
    [InlineData("fab/munich/plc")]
    [InlineData("fab/munich/plc/dev-1/extra")]
    [InlineData("plant/munich/plc/dev-1")]
    public void A_topic_of_the_wrong_shape_is_refused_with_the_topic_in_the_text(string topic)
    {
        var result = Parse(topic, Body(GoodEventId, "Alarm", """{"a":1}"""));

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe($"Unexpected MQTT topic shape: '{topic}'.");
    }

    [Fact]
    public void An_illegal_fab_segment_is_refused_with_the_fab_rule_message()
    {
        var result = Parse("fab/Munich/plc/dev-1", Good());

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe($"envelope parse failed: {MessageOf(() => FabIdentifier.From("Munich"))}");
    }

    [Fact]
    public void An_illegal_source_segment_is_refused_with_the_source_rule_message()
    {
        var result = Parse("fab/munich/robot/dev-1", Good());

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe($"envelope parse failed: {MessageOf(() => Source.From("robot"))}");
    }

    [Fact]
    public void An_illegal_device_segment_is_refused_with_the_device_rule_message()
    {
        var result = Parse("fab/munich/plc/-bad", Good());

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe($"envelope parse failed: {MessageOf(() => DeviceIdentifier.From("-bad"))}");
    }

    [Fact]
    public void A_body_that_is_not_json_is_refused_with_the_json_reader_message()
    {
        var text = "{not json";

        var result = Parse(GoodTopic, Encoding.UTF8.GetBytes(text));

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe($"envelope parse failed: {JsonMessageOf(text)}");
    }

    [Fact]
    public void A_body_that_is_json_null_is_refused_as_a_null_payload()
    {
        var result = Parse(GoodTopic, Encoding.UTF8.GetBytes("null"));

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe("envelope parse failed: payload is null");
    }

    [Fact]
    public void A_payload_the_payload_rule_rejects_is_refused_with_the_payload_prefix()
    {
        var body = Encoding.UTF8.GetBytes(
            $$"""{"eventId":"{{GoodEventId}}","kind":"Alarm","occurredAt":"2026-09-17T08:00:00Z"}""");

        var result = Parse(GoodTopic, body);

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe($"payload rejected: {MessageOf(() => Payload.From(default(JsonElement)))}");
    }

    [Fact]
    public void An_empty_event_identifier_is_refused_with_the_envelope_prefix()
    {
        var result = Parse(GoodTopic, Body(Guid.Empty.ToString(), "Alarm", """{"a":1}"""));

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe($"envelope rejected: {MessageOf(() => EventIdentifier.From(Guid.Empty))}");
    }

    [Fact]
    public void An_illegal_kind_is_refused_with_the_envelope_prefix()
    {
        var result = Parse(GoodTopic, Body(GoodEventId, "lowercase", """{"a":1}"""));

        result.Envelope.ShouldBeNull();
        result.Error.ShouldBe($"envelope rejected: {MessageOf(() => Kind.From("lowercase"))}");
    }

    [Fact]
    public void A_wrong_address_wins_over_a_body_that_is_not_json()
    {
        var result = Parse("fab/Munich/plc/dev-1", Encoding.UTF8.GetBytes("{not json"));

        result.Error.ShouldBe($"envelope parse failed: {MessageOf(() => FabIdentifier.From("Munich"))}");
    }

    [Fact]
    public void A_wrong_address_wins_over_a_json_null_body()
    {
        var result = Parse("fab/munich/robot/dev-1", Encoding.UTF8.GetBytes("null"));

        result.Error.ShouldBe($"envelope parse failed: {MessageOf(() => Source.From("robot"))}");
    }

    [Fact]
    public void The_fab_segment_is_judged_before_the_source_and_the_device()
    {
        var result = Parse("fab/Munich/robot/-bad", Good());

        result.Error.ShouldBe($"envelope parse failed: {MessageOf(() => FabIdentifier.From("Munich"))}");
    }

    [Fact]
    public void A_rejected_payload_wins_over_an_illegal_kind()
    {
        var body = Encoding.UTF8.GetBytes(
            $$"""{"eventId":"{{GoodEventId}}","kind":"lowercase","occurredAt":"2026-09-17T08:00:00Z"}""");

        var result = Parse(GoodTopic, body);

        result.Error.ShouldNotBeNull();
        result.Error.ShouldStartWith("payload rejected: ");
    }

    [Fact]
    public void An_empty_event_identifier_wins_over_an_illegal_kind()
    {
        var result = Parse(GoodTopic, Body(Guid.Empty.ToString(), "lowercase", """{"a":1}"""));

        result.Error.ShouldBe($"envelope rejected: {MessageOf(() => EventIdentifier.From(Guid.Empty))}");
    }

    [Fact]
    public void A_well_formed_delivery_maps_every_field_to_its_own_slot()
    {
        var eventId = Guid.Parse("01977f5a-6e3d-7c3a-8f1a-0000000000b7");
        var occurredAt = DateTimeOffset.Parse("2026-09-17T08:15:30Z", CultureInfo.InvariantCulture);
        var body = Body(eventId.ToString(), "SpecialKind", """{"note":"ok"}""", "2026-09-17T08:15:30Z");

        var result = Parse("fab/dresden/inference/line-9", body);

        result.Error.ShouldBeNull();
        var envelope = result.Envelope.ShouldNotBeNull();
        envelope.Identifier.ShouldBe(EventIdentifier.From(eventId));
        envelope.Fab.Value.ShouldBe("dresden");
        envelope.Source.ShouldBe(Source.Inference);
        envelope.Device.Value.ShouldBe("line-9");
        envelope.Kind.Value.ShouldBe("SpecialKind");
        envelope.OccurredAt.Value.ShouldBe(occurredAt);
        envelope.Payload.Value.ShouldBe("""{"note":"ok"}""");
    }

    private static MqttSubscriberHostedService.ParseResult Parse(string topic, byte[] body) =>
        MqttSubscriberHostedService.TryParseEnvelope(topic, body);

    private static byte[] Good() => Body(GoodEventId, "Alarm", """{"a":1}""");

    private static byte[] Body(
        string eventId,
        string kind,
        string payloadJson,
        string occurredAt = "2026-09-17T08:00:00Z") =>
        Encoding.UTF8.GetBytes(
            $$"""{"eventId":"{{eventId}}","kind":"{{kind}}","occurredAt":"{{occurredAt}}","payload":{{payloadJson}}}""");

    private static string MessageOf(Action action) =>
        Should.Throw<ArgumentException>(action).Message;

    private static string JsonMessageOf(string text) =>
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<MqttIngressPayload>(Encoding.UTF8.GetBytes(text))).Message;
}
