using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 394 / issue #3023, red. The two camera URL value objects live in two contexts and are meant to be the same
/// rule, and <see cref="UrlRedaction.ToOrigin"/> is meant to refuse to render exactly what they refuse to store.
/// Over one hand-tagged corpus (every row carries the outcome class the spec assigns it) this asserts, row by row
/// and unconditionally, that both <c>From</c>s give the tagged outcome, that nothing but an
/// <see cref="ArgumentException"/> escapes either, that what <c>From</c> refuses <c>ToOrigin</c> withholds, and
/// that what <c>From</c> accepts has a readable host and a rendered origin.
/// </summary>
public class CameraUrlZoneDelimiterRefusalTests
{
    private const string Accepted = "accepted";

    private const string Delimiter = "delimiter";

    private const string Unreadable = "unreadable-host";

    private const string UserInfo = "userinfo";

    private const string Older = "older-rules-other";

    private const string DelimiterRefusal =
        "value: must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host (Parameter 'value')";

    private const string UnreadableHostRefusal =
        "value: must name a host the parser can read: the address inside the brackets is malformed, for example a ':' left before a '/' prefix length (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    public static TheoryData<string, string> Corpus => new()
    {
        { "rtsp://[fe80::1%25eth0]/x", Accepted },
        { "rtsp://[fe80::1/64%25eth0]/s", Accepted },
        { "rtsp://[fe80::1%25eth0/64]/s", Accepted },
        { "rtsp://[fe80::1]/x", Accepted },
        { "rtsp://@[fe80::1]/x", Accepted },
        { "rtsp://@cam/s", Accepted },
        { "rtsp://[::1%25%40x]/s", Accepted },
        { "rtsp://[::1%25x%3Fy]/s", Accepted },
        { "rtsp://[fe80::1]?q", Accepted },
        { "rtsp://[fe80::1]#f", Accepted },
        { "rtsp://[fe80::1]:554/s", Accepted },
        { "rtsp://@[fe80::1%@10.0.0.66:8554]/live", Delimiter },
        { "rtsp://@[::1%25@evil.com:554]/s", Delimiter },
        { "rtsp://@[fe80::1%25eth0@]/s", Delimiter },
        { "rtsp://[::1%25x?y]/s", Delimiter },
        { "rtsp://[::1%25x#y]/s", Delimiter },
        { "rtsp://[fe80::1%25a?b]/x", Delimiter },
        { "rtsp://[fe80::1%x?y]/s", Delimiter },
        { "rtsp://[::1%25x?y]", Delimiter },
        { "rtsp://[fe80::1%25eth0#]:554/s", Delimiter },
        { "rtsp://@[::1%25x?y]/s", Delimiter },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]", Unreadable },
        { "rtsp://[::1:2:3:4:5:6:7:/64]/s", Unreadable },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]/live", Unreadable },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]x/s", Unreadable },
        { "RTSP://@[1:2:3:4:5:6:7:8:/64%25eth0]", Unreadable },
        { "RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s", UserInfo },
        { "rtsp://[]@[1:2:3:4:5:6:7:8:/64]x/s", UserInfo },
        { "rtsp://@[::1%25x?@evil.com]/s", Older },
        { "rtsp://[::1%25x?y]/a@b", Older },
        { "rtsp://[::1%25x?y]x/s", Older },
        { "rtsp://user@[::1%25@evil.com:554]/s", UserInfo },
        { "rtsp://[fe80::1%@10.0.0.66:8554]/live", UserInfo },
        { "rtsp://[::1%25@evil.com:554]/s", UserInfo },
    };

    public static TheoryData<string> Addresses => [.. Corpus.Cast<object[]>().Select(row => (string)row[0])];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Both_write_factories_give_the_tagged_outcome(string address, string tag)
    {
        var camera = Classify(Outcome(() => RtspUrl.From(address).Value));
        var stream = Classify(Outcome(() => StreamSourceUrl.From(address).Value));

        camera.ShouldBe(tag, address);
        stream.ShouldBe(tag, address);
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
    public void What_From_refuses_ToOrigin_withholds_and_what_From_accepts_it_shows(string address, string tag)
    {
        var origin = UrlRedaction.ToOrigin(address);

        if (tag == Accepted)
        {
            origin.ShouldNotBe(UrlRedaction.Unparseable, address);
        }
        else
        {
            origin.ShouldBe(UrlRedaction.Unparseable, address);
            Outcome(() => RtspUrl.From(address).Value).ShouldStartWith("refused: ", Case.Sensitive, address);
        }
    }

    [Fact]
    public void The_corpus_holds_every_outcome_class_row_by_row()
    {
        var rows = Corpus.Cast<object[]>().Select(row => ((string)row[0], (string)row[1])).ToList();

        rows.Count.ShouldBe(34);
        rows.Select(row => row.Item1).Distinct(StringComparer.Ordinal).Count().ShouldBe(34);
        rows.Count(row => row.Item2 == Accepted).ShouldBe(11);
        rows.Count(row => row.Item2 == Delimiter).ShouldBe(10);
        rows.Count(row => row.Item2 == Unreadable).ShouldBe(5);
        rows.Count(row => row.Item2 == UserInfo).ShouldBe(5);
        rows.Count(row => row.Item2 == Older).ShouldBe(3);

        foreach (var (address, tag) in rows)
        {
            Classify(Outcome(() => RtspUrl.From(address).Value)).ShouldBe(tag, address);
        }
    }

    private static string Classify(string outcome) => outcome switch
    {
        _ when outcome == "refused: " + DelimiterRefusal => Delimiter,
        _ when outcome == "refused: " + UnreadableHostRefusal => Unreadable,
        _ when outcome == "refused: " + UserInfoRefusal => UserInfo,
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
