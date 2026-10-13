using System.Text;
using SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Spec 173 (#2203) — <c>TryParseEnvelope</c> driven directly, without a
/// broker, MQTTnet, or a channel. The escape this covers happens before
/// acknowledgement, so nothing downstream of the parse can observe it: a
/// unit test against the parse itself is the only level that can assert
/// what the caught exception's message says (SC-3), rather than merely that
/// the delivery eventually stopped looping.
/// </summary>
public class MqttEnvelopeParsingTests
{
    private const string Topic = "fab/hamburg/plc/dev-1";

    [Fact]
    public void A_delivery_whose_payload_property_is_absent_is_rejected_rather_than_thrown()
    {
        var body = Encode("""
            {"eventId":"01977f5a-6e3d-7c3a-8f1a-000000000001","kind":"Alarm","occurredAt":"2026-09-17T08:00:00Z"}
            """);

        var result =
            MqttSubscriberHostedService.TryParseEnvelope(Topic, body);

        result.Envelope.ShouldBeNull();
        result.Error.ShouldNotBeNullOrEmpty();
        result.Error.ShouldContain("payload", Case.Insensitive);
    }

    [Fact]
    public void A_delivery_whose_payload_is_json_null_is_accepted()
    {
        var body = Encode("""
            {"eventId":"01977f5a-6e3d-7c3a-8f1a-000000000002","kind":"Alarm","occurredAt":"2026-09-17T08:00:00Z","payload":null}
            """);

        var result =
            MqttSubscriberHostedService.TryParseEnvelope(Topic, body);

        result.Envelope.ShouldNotBeNull();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void A_delivery_whose_payload_is_a_string_is_accepted()
    {
        var body = Encode("""
            {"eventId":"01977f5a-6e3d-7c3a-8f1a-000000000003","kind":"Alarm","occurredAt":"2026-09-17T08:00:00Z","payload":"oops"}
            """);

        var result =
            MqttSubscriberHostedService.TryParseEnvelope(Topic, body);

        result.Envelope.ShouldNotBeNull();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void A_well_formed_delivery_parses()
    {
        var body = Encode("""
            {"eventId":"01977f5a-6e3d-7c3a-8f1a-000000000004","kind":"Alarm","occurredAt":"2026-09-17T08:00:00Z","payload":{"note":"ok"}}
            """);

        var result =
            MqttSubscriberHostedService.TryParseEnvelope(Topic, body);

        result.Envelope.ShouldNotBeNull();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void A_payload_over_64_KB_is_rejected_with_a_readable_reason()
    {
        var overlong = "{\"data\":\"" + new string('a', 64 * 1024) + "\"}";
        var body = Encode(
            $$"""
            {"eventId":"01977f5a-6e3d-7c3a-8f1a-000000000005","kind":"Alarm","occurredAt":"2026-09-17T08:00:00Z","payload":{{overlong}}}
            """);

        var result =
            MqttSubscriberHostedService.TryParseEnvelope(Topic, body);

        result.Envelope.ShouldBeNull();
        result.Error.ShouldNotBeNullOrEmpty();
        result.Error.ShouldContain("payload", Case.Insensitive);
    }

    private static ReadOnlyMemory<byte> Encode(string json) => Encoding.UTF8.GetBytes(json);
}
