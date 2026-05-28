using System.Globalization;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class FabEventIngestedV1Tests
{
    private static readonly DateTimeOffset OccurredAt =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset IngestedAt =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Exposes_all_envelope_fields_via_the_positional_constructor()
    {
        Guid identifier = Guid.CreateVersion7();
        FabEventIngestedV1 evt = new(
            identifier, "munich", "plc", "station-4", "PlcCycleStart",
            OccurredAt, IngestedAt, "{\"cycleId\":\"abc\"}");

        evt.EventIdentifier.ShouldBe(identifier);
        evt.Fab.ShouldBe("munich");
        evt.Source.ShouldBe("plc");
        evt.Device.ShouldBe("station-4");
        evt.Kind.ShouldBe("PlcCycleStart");
        evt.OccurredAt.ShouldBe(OccurredAt);
        evt.IngestedAt.ShouldBe(IngestedAt);
        evt.Payload.ShouldBe("{\"cycleId\":\"abc\"}");
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        FabEventIngestedV1 evt = new(
            Guid.CreateVersion7(), "munich", "manual", "kiosk-3", "Annotation",
            OccurredAt, IngestedAt, "{}");
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid identifier = Guid.CreateVersion7();
        FabEventIngestedV1 a = new(
            identifier, "munich", "inference", "camera-12", "PersonInRestrictedZone",
            OccurredAt, IngestedAt, "{\"confidence\":0.92}");
        FabEventIngestedV1 b = new(
            identifier, "munich", "inference", "camera-12", "PersonInRestrictedZone",
            OccurredAt, IngestedAt, "{\"confidence\":0.92}");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_a_large_60KB_payload()
    {
        string largePayload =
            "{\"data\":\"" + new string('a', 60 * 1024) + "\"}";
        FabEventIngestedV1 original = new(
            Guid.CreateVersion7(), "munich", "webhook", "qa", "QaResult",
            OccurredAt, IngestedAt, largePayload);

        string json = JsonSerializer.Serialize(original);
        FabEventIngestedV1 deserialized =
            JsonSerializer.Deserialize<FabEventIngestedV1>(json)!;

        deserialized.ShouldBe(original);
        Encoding.UTF8.GetByteCount(deserialized.Payload).ShouldBeGreaterThan(60 * 1024);
    }
}
