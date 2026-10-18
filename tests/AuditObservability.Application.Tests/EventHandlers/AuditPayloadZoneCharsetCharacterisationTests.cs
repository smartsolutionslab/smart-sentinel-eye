using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Characterisation (spec 411 / issue #3023, A-1, ADR-0144), captured green on e5be6d72 before the camera URL value
// objects refuse a non-printable zone and a punctuation-only host. The audit consumer does not call From and is not
// taught either rule: a zoned pair already has an unknown DetailChanged in both directions (the zone arm sees the
// '%'), a pair of punctuation-only hosts still compares the rendered origins, and the stored Url is the origin.
// None of these may change.
public class AuditPayloadZoneCharsetCharacterisationTests
{
    private const string Ordinary = "rtsp://[fe80::1%25eth0]/s";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-18T08:00:00Z", CultureInfo.InvariantCulture);

    public static TheoryData<string> ZonesTheValueObjectsWillRefuse =>
    [
        "rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]/s",
        "rtsp://[fe80::1%25eth0%00]/s",
        "rtsp://[fe80::1/64%25eth0%0D]/s",
        "rtsp://[fe80::1%25eth0%E2%80%8E]/s",
        "rtsp://[fe80::1%25eth0‎]/s",
    ];

    [Theory]
    [MemberData(nameof(ZonesTheValueObjectsWillRefuse))]
    public void A_zone_the_value_objects_will_refuse_leaves_the_detail_claim_unknown_in_both_directions(string address)
    {
        var forward = JsonDocument.Parse(Serialize(Ordinary, address)).RootElement;
        var backward = JsonDocument.Parse(Serialize(address, Ordinary)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void A_registration_with_such_a_zone_stores_the_origin_without_the_zone()
    {
        var stored = AuditPayloadRedaction.Serialize(new CameraRegisteredV1(
            Guid.Empty,
            "n",
            "rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live",
            At,
            Guid.Empty,
            Metadata()));

        var json = JsonDocument.Parse(stored).RootElement;
        json.GetProperty("Url").GetString().ShouldBe("rtsp://[fe80::1]:8554");
        stored.ShouldNotContain("X-Inject", Case.Insensitive);
    }

    [Fact]
    public void A_change_between_two_punctuation_only_hosts_still_compares_the_origins()
    {
        var same = JsonDocument.Parse(Serialize("rtsp://../a", "rtsp://../b")).RootElement;
        var other = JsonDocument.Parse(Serialize("rtsp://../a", "rtsp://./a")).RootElement;

        same.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://..");
        same.GetProperty("Url").GetString().ShouldBe("rtsp://..");
        same.GetProperty("DetailChanged").GetBoolean().ShouldBeTrue();
        other.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.False);
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        ZonesTheValueObjectsWillRefuse.Cast<object[]>().Count().ShouldBe(5);
    }

    private static string Serialize(string previous, string url) =>
        AuditPayloadRedaction.Serialize(new CameraAddressChangedV1(Guid.Empty, "fab", previous, url, At, Guid.Empty, Metadata()));

    private static EventMetadata Metadata() => new(Guid.Empty, At, "fab", null);
}
