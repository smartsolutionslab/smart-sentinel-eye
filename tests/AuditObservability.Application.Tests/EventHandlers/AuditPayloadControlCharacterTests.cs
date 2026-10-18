using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Spec 408 / issue #3031 item 1, FR-002 and US1a, red. The audit serialiser renders the placeholder for an address
// that holds a control character or whitespace inside its text, on a registration and on each side of an address
// change, and a change to or from such an address has an unknown DetailChanged (null), where it used to claim
// "same host, different detail" (true) for an address no write accepts. The payload carries nothing of the
// address past the placeholder. Receives the retired row of spec 408 A-2: the zone holding CR LF and CSeq that
// AuditPayloadZoneDelimiterAdversarialTests used to expect as rtsp://[fe80::1].
public class AuditPayloadControlCharacterTests
{
    private const string Ordinary = "rtsp://10.0.0.66/live";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-18T08:00:00Z", CultureInfo.InvariantCulture);

    public static TheoryData<string, string> Interior => new()
    {
        { "rtsp://10.0.0.66/live\r\nX-Inject: 1", "X-Inject" },
        { "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live", "X-Inject" },
        { "rtsp://10.0.0.66/live?token=a\tSECRET", "SECRET" },
        { "rtsp://10.0.0.66/live\u0000SECRET", "SECRET" },
        { "rtsp://10.0.0.66/pa th", "pa th" },
        { "rtsp://10.0.0.66/a\x00A0SECRET", "SECRET" },
        { " rtsp://10.0.0.66/pa th ", "pa th" },
    };

    [Theory]
    [MemberData(nameof(Interior))]
    public void A_registration_over_such_an_address_stores_the_placeholder_and_none_of_it(string address, string marker)
    {
        var stored = AuditPayloadRedaction.Serialize(
            new CameraRegisteredV1(Guid.Empty, "n", address, At, Guid.Empty, Metadata()));

        JsonDocument.Parse(stored).RootElement.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        stored.ShouldNotContain(marker, Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(Interior))]
    public void A_change_to_such_an_address_stores_the_placeholder_for_it_and_an_unknown_detail_change(
        string address,
        string marker)
    {
        var stored = Serialize(Ordinary, address);

        var json = JsonDocument.Parse(stored).RootElement;
        json.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://10.0.0.66");
        json.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        stored.ShouldNotContain(marker, Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(Interior))]
    public void A_change_from_such_an_address_stores_the_placeholder_for_it_and_an_unknown_detail_change(
        string address,
        string marker)
    {
        var stored = Serialize(address, Ordinary);

        var json = JsonDocument.Parse(stored).RootElement;
        json.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("Url").GetString().ShouldBe("rtsp://10.0.0.66");
        json.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        stored.ShouldNotContain(marker, Case.Insensitive);
    }

    [Fact]
    public void A_zoned_pair_with_a_line_break_stores_the_placeholder_for_that_side_and_an_unknown_detail_change()
    {
        var stored = Serialize("rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live", "rtsp://[fe80::1]:8554/live");

        var json = JsonDocument.Parse(stored).RootElement;
        json.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("Url").GetString().ShouldBe("rtsp://[fe80::1]:8554");
        json.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        stored.ShouldNotContain("X-Inject", Case.Insensitive);
    }

    [Fact]
    public void A_zone_holding_a_line_break_is_withheld_whole_not_cut_down_to_its_address()
    {
        const string address = "rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s";

        var stored = AuditPayloadRedaction.Serialize(
            new CameraRegisteredV1(Guid.Empty, "n", address, At, Guid.Empty, Metadata()));

        JsonDocument.Parse(stored).RootElement.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        stored.ShouldNotContain("CSeq", Case.Insensitive);
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        Interior.Cast<object[]>().Count().ShouldBe(7);
    }

    private static string Serialize(string previous, string url) =>
        AuditPayloadRedaction.Serialize(new CameraAddressChangedV1(Guid.Empty, "fab", previous, url, At, Guid.Empty, Metadata()));

    private static EventMetadata Metadata() => new(Guid.Empty, At, "fab", null);
}
