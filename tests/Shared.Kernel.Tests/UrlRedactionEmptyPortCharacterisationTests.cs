namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Characterisation (spec 414 / issue #3031 item 3, A-1, ADR-0144), captured green on <c>1cbae37d</c> before the
/// camera URL value objects learn to refuse a ':' that names no port. <see cref="UrlRedaction.ToOrigin"/> is
/// deliberately not taught the rule: every reader reads the same host, and the origin is the host plus the default
/// port that the parser dials, so it renders these addresses without a port. <c>From</c> refuses the rows while the
/// origin still renders them (the recorded disagreement). None of these rows may be edited by the change.
/// </summary>
public class UrlRedactionEmptyPortCharacterisationTests
{
    public static TheoryData<string, string> AsciiRows => new()
    {
        { "rtsp://10.0.0.66:/live", "rtsp://10.0.0.66" },
        { "rtsp://[::1]:/live", "rtsp://[::1]" },
        { "rtsp://cam:", "rtsp://cam" },
        { "rtsp://cam:/x", "rtsp://cam" },
        { "rtsp://cam:?q", "rtsp://cam" },
        { "rtsp://cam:#f", "rtsp://cam" },
        { "rtsp://@cam:/x", "rtsp://cam" },
        { "rtsp://cam.:/x", "rtsp://cam." },
        { "RTSP://CAM:/S", "rtsp://cam" },
        { "rtsp://cam:/x?a=b", "rtsp://cam" },
        { "rtsp://xn--kamera-mnchen-wob.local:/live", "rtsp://xn--kamera-mnchen-wob.local" },
        { "rtsp://[fe80::1]:", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1]:/", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1]:?q", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1]:#f", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1]:/s", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1]:/s?x#y", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1%25eth0]:/s", "rtsp://[fe80::1]" },
        { "rtsp://@[::1]:/x", "rtsp://[::1]" },
        { "rtsp://[::ffff:10.0.0.1]:/s", "rtsp://[::ffff:10.0.0.1]" },
        { "rtsp://[fe80::1/64]:/x", "rtsp://[fe80::1]" },
        { "rtsp://[fe80::1/64%25eth0]:/x", "rtsp://[fe80::1]" },
        { "rtsp://[1:2:3:4:5:6:7::/64]:/s", "rtsp://[1:2:3:4:5:6:7:0]" },
    };

    [Theory]
    [MemberData(nameof(AsciiRows))]
    public void An_address_with_a_colon_and_no_port_still_renders_its_origin_without_a_port(string url, string expected)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(expected);
    }

    [Fact]
    public void An_address_whose_host_carries_an_invisible_character_is_already_the_placeholder()
    {
        var url = "rtsp://cam" + char.ConvertFromUtf32(0x200E) + ":/p";

        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        AsciiRows.Cast<object[]>().Count().ShouldBe(23);
    }
}
