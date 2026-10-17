using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Spec 394 / issue #3023 items 1, 2 and 5, adversarial. Variants beyond the written rows reach the audit serialiser
// from pre-deploy or replayed events: a delimiter behind a prefix length or after a raw '%' zone, an uppercase
// scheme with an '@' in the zone, unreadable hosts behind a query, a zone or an '@'. The serialiser never throws,
// the stored payload carries nothing of the hidden host on a registration or on either side of an address change,
// and DetailChanged is null (unknown) whenever a side was withheld. A zone holding CR LF, which the value objects
// still accept, is dropped with the rest of the zone from the origin.
public class AuditPayloadZoneDelimiterAdversarialTests
{
    private const string Ordinary = "rtsp://[fe80::1]/x";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-17T08:00:00Z", CultureInfo.InvariantCulture);

    public static TheoryData<string, string> Withheld => new()
    {
        { "rtsp://[fe80::1/64%25x?y]/s", "x?y" },
        { "rtsp://[fe80::1%?evil.com]/s", "evil" },
        { "RTSP://@[FE80::1%25ETH0@10.0.0.66]/s", "10.0.0.66" },
        { "rtsp://[fe80::1%25 ?evil]/s", "evil" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]?x", "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:%25eth0/64]/s", "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]@evil.com/s", "evil" },
        { "rtsp://[]@[1:2:3:4:5:6:7:8:/64]x/s", "1:2:3:4" },
    };

    [Theory]
    [MemberData(nameof(Withheld))]
    public void A_registration_over_the_variant_stores_the_placeholder_and_none_of_it(string address, string secret)
    {
        var stored = AuditPayloadRedaction.Serialize(
            new CameraRegisteredV1(Guid.Empty, "n", address, At, Guid.Empty, Metadata()));

        JsonDocument.Parse(stored).RootElement.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        stored.ShouldNotContain(secret, Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(Withheld))]
    public void An_address_change_on_either_side_stores_the_placeholder_and_an_unknown_detail_change(string address, string secret)
    {
        foreach (var (previous, url) in new[] { (address, Ordinary), (Ordinary, address), (address, address) })
        {
            var stored = AuditPayloadRedaction.Serialize(
                new CameraAddressChangedV1(Guid.Empty, "fab", previous, url, At, Guid.Empty, Metadata()));

            stored.ShouldNotContain(secret, Case.Insensitive);
            JsonDocument.Parse(stored).RootElement.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Fact]
    public void A_zone_holding_a_line_break_is_dropped_from_the_origin_with_the_rest_of_the_zone()
    {
        const string address = "rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s";

        var stored = AuditPayloadRedaction.Serialize(
            new CameraRegisteredV1(Guid.Empty, "n", address, At, Guid.Empty, Metadata()));

        JsonDocument.Parse(stored).RootElement.GetProperty("Url").GetString().ShouldBe("rtsp://[fe80::1]");
        stored.ShouldNotContain("CSeq", Case.Insensitive);
    }

    private static EventMetadata Metadata() => new(Guid.Empty, At, "fab", null);
}
