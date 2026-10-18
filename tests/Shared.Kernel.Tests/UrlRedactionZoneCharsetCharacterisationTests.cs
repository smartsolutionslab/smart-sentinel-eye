namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Characterisation (spec 411 / issue #3023, A-1, ADR-0144), captured green on <c>e5be6d72</c> before the camera URL
/// value objects learn to refuse a non-printable zone and a punctuation-only host. <see cref="UrlRedaction.ToOrigin"/>
/// is deliberately not taught either rule: the origin always drops the zone, so what it renders is the host every
/// reader dials, and <c>rtsp://..</c> renders what was written. <c>From</c> refuses these rows while the origin still
/// renders them (the recorded disagreement). None of these rows may be edited by the change.
/// </summary>
public class UrlRedactionZoneCharsetCharacterisationTests
{
    public static TheoryData<string, string> AsciiZones => new()
    {
        { "rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live", "rtsp://[fe80::1]:8554" },
        { "rtsp://@[fe80::1%25eth0%0D]/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1/64%25eth0%0D]/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1%25eth0%00SECRET]/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1%eth0%0D]/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1%25eth0%7F]/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1%25eth0%20x]/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1%25eth0%C2%85]/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1%25eth0%E2%80%8E]/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1%25%20]/x", "rtsp://[fe80::1]" },
    };

    public static TheoryData<int> RawNonAsciiInTheZone =>
    [
        0x200E, 0x202E, 0xFEFF, 0x200B, 0x3164, 0x115F, 0x1160, 0xFFA0, 0x2800, 0x00FC, 0xFF45,
    ];

    public static TheoryData<string, string> PunctuationHosts => new()
    {
        { "rtsp://./", "rtsp://." },
        { "rtsp://.", "rtsp://." },
        { "rtsp://..", "rtsp://.." },
        { "rtsp://../live", "rtsp://.." },
        { "rtsp://..?x", "rtsp://.." },
        { "rtsp://..#x", "rtsp://.." },
        { "rtsp://-/x", "rtsp://-" },
        { "rtsp://_/x", "rtsp://_" },
        { "rtsp://-._/x", "rtsp://-._" },
        { "RTSP://../x", "rtsp://.." },
    };

    [Theory]
    [MemberData(nameof(AsciiZones))]
    public void An_ascii_zone_the_value_objects_are_about_to_refuse_still_renders_the_host_without_its_zone(
        string url,
        string expected)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(RawNonAsciiInTheZone))]
    public void A_raw_non_ascii_character_in_the_zone_is_already_the_placeholder(int codePoint)
    {
        var url = $"rtsp://[fe80::1%25eth0{char.ConvertFromUtf32(codePoint)}]/s";

        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable, $"U+{codePoint:X4} in zone");
    }

    [Theory]
    [MemberData(nameof(PunctuationHosts))]
    public void A_punctuation_only_host_still_renders_what_was_written(string url, string expected)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(expected);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        AsciiZones.Cast<object[]>().Count().ShouldBe(10);
        RawNonAsciiInTheZone.Cast<object[]>().Count().ShouldBe(11);
        PunctuationHosts.Cast<object[]>().Count().ShouldBe(10);
    }
}
