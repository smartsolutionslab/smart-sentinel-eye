using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 392 / issues #3012 and #3017, red. <see cref="RtspUrl.From"/> refuses an address whose host the parser
/// reads as empty (everything after a third '/' is a path, so a password written there is stored in clear) and
/// an address with text after the closing ']' of an IPv6 host (the parser drops it from the host). Each refusal
/// has its own message, identical in <c>StreamSourceUrl</c>, and carries no part of the address.
/// <see cref="RtspUrl.FromStored"/> keeps reading both back (pinned by
/// <c>RtspUrlFromStoredAuthorityHostTests</c>).
/// </summary>
public class RtspUrlAuthorityHostTests
{
    private const string EmptyHostRefusal =
        "value: must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    public static TheoryData<string, string> EmptyHostAddresses => new()
    {
        { "rtsp://", "n/a" },
        { "rtsp:///", "n/a" },
        { "rtsp:///cam", "/cam" },
        { "RTSP:///cam", "/cam" },
        { "rtsp:///admin:4242@cam/s", "4242" },
        { "rtsp:////admin:4242@cam/s", "4242" },
        { "rtsp://?x", "n/a" },
        { "rtsp://#x", "n/a" },
        { "rtsp:///?x", "n/a" },
    };

    public static TheoryData<string, string> BracketTailAddresses => new()
    {
        { "rtsp://[fe80::1]%25eth0/x", "eth0" },
        { "rtsp://[fe80::1]%eth0/x", "eth0" },
        { "rtsp://[fe80::1]garbage.example/x", "garbage.example" },
        { "rtsp://[fe80::1]garbage.example", "garbage.example" },
        { "rtsp://[fe80::1]x:554/s", "x:554" },
        { "rtsp://[fe80::1]]/x", "n/a" },
        { "RTSP://[FE80::1]GARBAGE/x", "GARBAGE" },
        { "rtsp://[::ffff:10.0.0.1]x/s", "n/a" },
        { "rtsp://[fe80::1/64]x/s", "n/a" },
        { "rtsp://[fe80::1]%20/s", "%20" },
    };

    [Theory]
    [MemberData(nameof(EmptyHostAddresses))]
    public void From_refuses_an_address_with_an_empty_host_and_says_what_to_fix(string address, string secret)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(EmptyHostRefusal);
        refused.ParamName.ShouldBe("value");
        AssertNothingOfTheAddress(refused, secret);
    }

    [Theory]
    [MemberData(nameof(BracketTailAddresses))]
    public void From_refuses_text_after_the_closing_bracket_and_says_what_to_fix(string address, string secret)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(BracketTailRefusal);
        refused.ParamName.ShouldBe("value");
        AssertNothingOfTheAddress(refused, secret);
    }

    private static void AssertNothingOfTheAddress(ArgumentException refused, string secret)
    {
        if (secret != "n/a")
        {
            refused.ToString().ShouldNotContain(secret, Case.Sensitive);
        }
    }
}
