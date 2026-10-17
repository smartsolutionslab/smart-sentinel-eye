using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 392 / issues #3012 and #3017, red. The two camera URL value objects live in two contexts and are meant to
/// be the same rule, and <see cref="UrlRedaction.ToOrigin"/> is meant to refuse to render exactly what they
/// refuse to store. Over one hand-tagged corpus this asserts that the two <c>From</c>s give the same answer, that
/// whatever <c>From</c> refuses on the empty-host or the text-after-bracket rule <c>ToOrigin</c> renders as the
/// placeholder, and that whatever <c>From</c> accepts has a host the parser read and an origin that is not the
/// placeholder. The first test is issue #3017's own, verbatim.
/// </summary>
public class CameraUrlAuthorityHostRefusalTests
{
    private const string EmptyHostRefusal =
        "value: must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    public static TheoryData<string> Corpus =>
    [
        "rtsp://",
        "rtsp:///",
        "rtsp:///cam",
        "RTSP:///cam",
        "rtsp:///admin:4242@cam/s",
        "rtsp:////admin:4242@cam/s",
        "rtsp://?x",
        "rtsp://#x",
        "rtsp:///?x",
        "rtsp://[fe80::1]%25eth0/x",
        "rtsp://[fe80::1]%eth0/x",
        "rtsp://[fe80::1]garbage.example/x",
        "rtsp://[fe80::1]garbage.example",
        "rtsp://[fe80::1]x:554/s",
        "rtsp://[fe80::1]]/x",
        "RTSP://[FE80::1]GARBAGE/x",
        "rtsp://[::ffff:10.0.0.1]x/s",
        "rtsp://[fe80::1/64]x/s",
        "rtsp://[fe80::1]%20/s",
        "rtsp:///admin:4242#@cam",
        "rtsp:////admin:4242#@cam",
        "rtsp://[fe80::1]:554x/s",
        "rtsp://[fe80::1]\\x",
        "rtsp://:554/cam",
        "rtsp://u@[fe80::1]x/s",
        "rtsp://[fe80::1]evil@cam/x",
        "rtsp:\\\\[fe80::1]garbage/x",
        "rtsp://10.0.5.12/h264",
        "rtsp://cam:554/path?x=y",
        "RTSP://CAM/s",
        "rtsp://@cam/s",
        "rtsp://[::1]:554/s",
        "rtsp://[fe80::1]/s",
        "rtsp://[fe80::1]",
        "rtsp://[fe80::1]:554/s",
        "rtsp://[fe80::1]:0554/s",
        "rtsp://[fe80::1]:/s",
        "rtsp://[fe80::1]?q",
        "rtsp://[fe80::1]#f",
        "rtsp://[fe80::1%25eth0]/x",
        "rtsp://[fe80::1/64%25eth0]/s",
        "rtsp://[::ffff:10.0.0.1]/x",
    ];

    [Theory]
    [InlineData("rtsp:///admin:4242@cam/s")]
    [InlineData("rtsp:////admin:4242@cam/s")]
    public void An_empty_authority_followed_by_userinfo_looking_text_is_refused(string address)
    {
        Should.Throw<ArgumentException>(() => RtspUrl.From(address));
        Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_two_write_factories_give_the_same_answer(string address)
    {
        Outcome(() => RtspUrl.From(address).Value).ShouldBe(Outcome(() => StreamSourceUrl.From(address).Value));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Whatever_From_refuses_on_the_new_rules_ToOrigin_will_not_render(string address)
    {
        var outcome = Outcome(() => RtspUrl.From(address).Value);

        if (outcome == "refused: " + EmptyHostRefusal || outcome == "refused: " + BracketTailRefusal)
        {
            UrlRedaction.ToOrigin(address).ShouldBe(UrlRedaction.Unparseable);
        }
    }

    [Theory]
    [InlineData("rtsp://[fe80::1]%25eth0/x")]
    [InlineData("rtsp://[fe80::1]%eth0/x")]
    [InlineData("rtsp://[fe80::1]garbage.example/x")]
    [InlineData("rtsp://[fe80::1]garbage.example")]
    [InlineData("rtsp://[fe80::1]x:554/s")]
    [InlineData("rtsp://[fe80::1]]/x")]
    [InlineData("RTSP://[FE80::1]GARBAGE/x")]
    [InlineData("rtsp://[::ffff:10.0.0.1]x/s")]
    [InlineData("rtsp://[fe80::1/64]x/s")]
    [InlineData("rtsp://[fe80::1]%20/s")]
    public void An_address_with_text_after_the_closing_bracket_is_both_refused_and_withheld(string address)
    {
        Outcome(() => RtspUrl.From(address).Value).ShouldBe("refused: " + BracketTailRefusal);
        Outcome(() => StreamSourceUrl.From(address).Value).ShouldBe("refused: " + BracketTailRefusal);
        UrlRedaction.ToOrigin(address).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Whatever_From_accepts_has_a_host_the_parser_read_and_an_origin_that_is_shown(string address)
    {
        var outcome = Outcome(() => RtspUrl.From(address).Value);

        if (outcome.StartsWith("accepted: ", StringComparison.Ordinal))
        {
            Uri.TryCreate(address, UriKind.Absolute, out var uri).ShouldBeTrue();
            uri.Host.ShouldNotBeEmpty();
            UrlRedaction.ToOrigin(address).ShouldNotBe(UrlRedaction.Unparseable);
        }
    }

    [Fact]
    public void The_corpus_exercises_all_three_outcomes()
    {
        var outcomes = Corpus
            .Cast<object[]>()
            .Select(row => (string)row[0])
            .Select(address => Outcome(() => RtspUrl.From(address).Value))
            .ToList();

        outcomes.Count(outcome => outcome == "refused: " + EmptyHostRefusal).ShouldBe(9);
        outcomes.Count(outcome => outcome == "refused: " + BracketTailRefusal).ShouldBe(10);
        outcomes.Count(outcome => outcome.StartsWith("accepted: ", StringComparison.Ordinal)).ShouldBe(16);
    }

    private static string Outcome(Func<string> read)
    {
        try
        {
            return "accepted: " + read();
        }
        catch (ArgumentException refused)
        {
            return "refused: " + refused.Message;
        }
    }
}
