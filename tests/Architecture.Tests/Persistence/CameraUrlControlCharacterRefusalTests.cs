using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 408 / issue #3031 item 1, FR-006, red. The two camera URL value objects live in two contexts and are meant
/// to be the same rule, and <see cref="UrlRedaction.ToOrigin"/> is meant to withhold what they refuse to store,
/// except at the parser-trimmed edges (A-1). Over one hand-tagged corpus (every row carries the outcome class the
/// spec assigns it) this asserts, row by row and unconditionally, that both <c>From</c>s give the tagged outcome and
/// the same message, that nothing but an <see cref="ArgumentException"/> escapes either, that a refused row with a
/// non-member at both ends renders the placeholder, and that an edge-only row is refused by <c>From</c> while its
/// origin is still rendered (the recorded disagreement, R-2).
/// </summary>
public class CameraUrlControlCharacterRefusalTests
{
    private const string Accepted = "accepted";

    private const string Interior = "control-or-white-space-inside";

    private const string Edge = "control-or-white-space-at-an-edge";

    private const string Older = "older-rule";

    private const string ControlOrWhiteSpaceRefusal =
        "value: must not contain a control character or whitespace anywhere: a line break, tab or space is copied into the request a reader sends. Percent-encode a space in a path or query as %20 (Parameter 'value')";

    public static TheoryData<string, string> Corpus => new()
    {
        { "rtsp://10.0.5.12/h264", Accepted },
        { "rtsp://[fe80::1%25eth0]:8554/x", Accepted },
        { "rtsp://cam/x%0D%0Ay%20z", Accepted },
        { "rtsp://cam/li\x200Bve", Accepted },
        { "rtsp://10.0.0.66/live\r\nX-Inject: 1", Interior },
        { "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live", Interior },
        { "rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s", Interior },
        { "rtsp://10.0.0.66/live?token=a\tSECRET", Interior },
        { "rtsp://10.0.0.66/live\u0000SECRET", Interior },
        { "rtsp://10.0.0.66/li ve", Interior },
        { "rtsp://10.0.0.66/live#f x", Interior },
        { "rtsp://@cam/li\u0085ve", Interior },
        { "rtsp://[fe80::1%25e th0]/x", Interior },
        { "rtsp://10.0.0.66/live\u007Fx", Interior },
        { "rtsp://10.0.0.66/a\x3000b", Interior },
        { "rtsp://cam/a\x00A0b", Interior },
        { "rtsp://[fe80::1]/x ", Edge },
        { "rtsp://cam ", Edge },
        { "RTSP://cam/x ", Edge },
        { "rtsp://10.0.0.66/live\t", Edge },
        { "rtsp://cam/s\u0000", Edge },
        { "rtsp://a b/x", Older },
        { "rtsp://[fe80::1] /x", Older },
        { "rtsp://admin:4242#@cam/s ", Older },
        { "rtsp://[fe80::1%25 ?evil]/s", Older },
    };

    public static TheoryData<string> Addresses => [.. Corpus.Cast<object[]>().Select(row => (string)row[0])];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Both_write_factories_give_the_tagged_outcome(string address, string tag)
    {
        var camera = Classify(Outcome(() => RtspUrl.From(address).Value));
        var stream = Classify(Outcome(() => StreamSourceUrl.From(address).Value));

        camera.ShouldBe(Expected(tag), address);
        stream.ShouldBe(Expected(tag), address);
    }

    [Theory]
    [MemberData(nameof(Addresses))]
    public void The_two_write_factories_agree_on_the_outcome_and_the_message(string address)
    {
        Outcome(() => StreamSourceUrl.From(address).Value).ShouldBe(Outcome(() => RtspUrl.From(address).Value), address);
    }

    [Theory]
    [MemberData(nameof(Addresses))]
    public void Nothing_but_an_ArgumentException_escapes_either_write_factory(string address)
    {
        Outcome(() => RtspUrl.From(address).Value).ShouldNotStartWith("threw: ", Case.Sensitive, address);
        Outcome(() => StreamSourceUrl.From(address).Value).ShouldNotStartWith("threw: ", Case.Sensitive, address);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void What_From_refuses_for_a_character_inside_the_text_ToOrigin_withholds(string address, string tag)
    {
        var origin = UrlRedaction.ToOrigin(address);

        switch (tag)
        {
            case Accepted:
                origin.ShouldNotBe(UrlRedaction.Unparseable, address);
                break;
            case Edge:
                origin.ShouldNotBe(UrlRedaction.Unparseable, address);
                Outcome(() => RtspUrl.From(address).Value).ShouldBe("refused: " + ControlOrWhiteSpaceRefusal, address);
                break;
            default:
                origin.ShouldBe(UrlRedaction.Unparseable, address);
                Outcome(() => RtspUrl.From(address).Value).ShouldStartWith("refused: ", Case.Sensitive, address);
                break;
        }
    }

    [Fact]
    public void The_corpus_holds_every_outcome_class_row_by_row()
    {
        var rows = Corpus.Cast<object[]>().Select(row => ((string)row[0], (string)row[1])).ToList();

        rows.Count.ShouldBe(25);
        rows.Select(row => row.Item1).Distinct(StringComparer.Ordinal).Count().ShouldBe(25);
        rows.Count(row => row.Item2 == Accepted).ShouldBe(4);
        rows.Count(row => row.Item2 == Interior).ShouldBe(12);
        rows.Count(row => row.Item2 == Edge).ShouldBe(5);
        rows.Count(row => row.Item2 == Older).ShouldBe(4);
    }

    private static string Expected(string tag) => tag switch
    {
        Accepted => Accepted,
        Older => Older,
        _ => "refused-for-control-or-white-space",
    };

    private static string Classify(string outcome) => outcome switch
    {
        _ when outcome == "refused: " + ControlOrWhiteSpaceRefusal => "refused-for-control-or-white-space",
        _ when outcome.StartsWith("threw: ", StringComparison.Ordinal) => outcome,
        _ when outcome.StartsWith("accepted: ", StringComparison.Ordinal) => Accepted,
        _ => Older,
    };

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
        catch (Exception escaped)
        {
            return "threw: " + escaped.GetType().Name;
        }
    }
}
