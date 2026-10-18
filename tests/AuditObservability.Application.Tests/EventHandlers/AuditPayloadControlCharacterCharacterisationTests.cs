using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Characterisation (spec 408 / issue #3031 item 1, FR-002 and A-1, ADR-0144), captured green on a73df3f1 before
// ToOrigin withholds a control character or whitespace inside the text. A zoned address with a line break already
// has an unknown DetailChanged in both directions (the zone arm sees the '%'); a character only at the start or
// end of an address is outside the text, so its origin is still rendered and a change that adds one is still a
// detail change; percent-encoded and Unicode format characters are not raw characters. None of these may change.
public class AuditPayloadControlCharacterCharacterisationTests
{
    private const string Ordinary = "rtsp://[fe80::1]:8554/live";

    private const string ZonedWithLineBreak = "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-18T08:00:00Z", CultureInfo.InvariantCulture);

    public static TheoryData<string, string, string, string> EdgeOnlyChanges => new()
    {
        { "rtsp://cam/x", "rtsp://cam/x ", "rtsp://cam", "rtsp://cam" },
        { "rtsp://cam/x", "rtsp://cam/x\t", "rtsp://cam", "rtsp://cam" },
        { "rtsp://10.0.0.66/live", "rtsp://10.0.0.66/live\r\n", "rtsp://10.0.0.66", "rtsp://10.0.0.66" },
        { " rtsp://cam/s", "rtsp://cam/s", "rtsp://cam", "rtsp://cam" },
        { "rtsp://cam/s\u0000", "rtsp://cam/t", "rtsp://cam", "rtsp://cam" },
    };

    public static TheoryData<string, string, string> NotRawCharacters => new()
    {
        { "rtsp://cam/x", "rtsp://cam/x%0D%0AX", "rtsp://cam" },
        { "rtsp://cam/x", "rtsp://cam/x%20y", "rtsp://cam" },
        { "rtsp://cam/x", "rtsp://cam/li\x200Bve", "rtsp://cam" },
        { "rtsp://cam/x", "rtsp://cam/li\x202Eve", "rtsp://cam" },
        { "rtsp://cam/x", "rtsp://cam/li\xFEFFve", "rtsp://cam" },
    };

    [Theory]
    [MemberData(nameof(EdgeOnlyChanges))]
    public void A_change_that_only_adds_or_drops_an_edge_character_stays_a_detail_change_with_the_origin_rendered(
        string previous,
        string url,
        string previousOrigin,
        string origin)
    {
        var stored = Serialize(previous, url);

        var json = JsonDocument.Parse(stored).RootElement;
        json.GetProperty("PreviousUrl").GetString().ShouldBe(previousOrigin);
        json.GetProperty("Url").GetString().ShouldBe(origin);
        json.GetProperty("DetailChanged").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(NotRawCharacters))]
    public void A_change_to_a_percent_encoded_or_format_character_address_renders_the_origin_and_is_a_detail_change(
        string previous,
        string url,
        string origin)
    {
        var stored = Serialize(previous, url);

        var json = JsonDocument.Parse(stored).RootElement;
        json.GetProperty("Url").GetString().ShouldBe(origin);
        json.GetProperty("DetailChanged").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void A_zoned_address_with_a_line_break_has_an_unknown_detail_change_when_it_is_the_new_address()
    {
        var stored = Serialize(Ordinary, ZonedWithLineBreak);

        JsonDocument.Parse(stored).RootElement.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void A_zoned_address_with_a_line_break_has_an_unknown_detail_change_when_it_is_the_previous_address()
    {
        var stored = Serialize(ZonedWithLineBreak, Ordinary);

        JsonDocument.Parse(stored).RootElement.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void An_ordinary_registration_keeps_its_origin()
    {
        var stored = AuditPayloadRedaction.Serialize(
            new CameraRegisteredV1(Guid.Empty, "n", "rtsp://10.0.5.12/h264", At, Guid.Empty, Metadata()));

        JsonDocument.Parse(stored).RootElement.GetProperty("Url").GetString().ShouldBe("rtsp://10.0.5.12");
    }

    private static string Serialize(string previous, string url) =>
        AuditPayloadRedaction.Serialize(new CameraAddressChangedV1(Guid.Empty, "fab", previous, url, At, Guid.Empty, Metadata()));

    private static EventMetadata Metadata() => new(Guid.Empty, At, "fab", null);
}
