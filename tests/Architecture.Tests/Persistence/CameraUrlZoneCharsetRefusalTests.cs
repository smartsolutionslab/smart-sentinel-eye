using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 411 / issue #3023, FR-006, red. The two camera URL value objects live in two contexts and are meant to be the
/// same rule. Over one hand-tagged corpus (every row carries the outcome class the spec assigns it) this asserts, row
/// by row and unconditionally, that both <c>From</c>s give the tagged outcome and the same message, that nothing but
/// an <see cref="ArgumentException"/> escapes either, and that an address an older rule refuses keeps that rule's
/// message because the new rules are last.
/// </summary>
public class CameraUrlZoneCharsetRefusalTests
{
    private const string Accepted = "accepted";

    private const string Zone = "non-printable-zone";

    private const string Punctuation = "punctuation-only-host";

    private const string Older = "older-rule";

    private const string ZoneRefusal =
        "value: must hold only printable ASCII in the zone of an IPv6 address (after the '%'), written or percent-encoded: a control character, space or invisible character there is decoded by some readers and not by others (Parameter 'value')";

    private const string PunctuationRefusal =
        "value: must name a host holding a letter or digit: a host of only '.', '-' or '_' names no machine (Parameter 'value')";

    public static TheoryData<string, string> Corpus
    {
        get
        {
            TheoryData<string, string> rows = [];
            foreach (var address in new[]
                     {
                         "rtsp://[fe80::1%25eth0]:8554/x", "rtsp://[fe80::1%2512]/s", "rtsp://[fe80::1%eth0]/s",
                         "rtsp://[fe80::1%25eth0%zz]/s", "rtsp://[fe80::1%25]/s", "rtsp://[fe80::1/64%25eth0]/s",
                         "rtsp://cam./x", "rtsp://cam_1/x", "rtsp://a-b/x", "rtsp://0/x", "rtsp://[::]/x",
                     })
            {
                rows.Add(address, Accepted);
            }

            foreach (var address in ZoneCharsetRows.Named.Select(row => row.Address)
                         .Concat(ZoneCharsetRows.EncodedOctets.Select(ZoneCharsetRows.Encoded))
                         .Concat(ZoneCharsetRows.RawCodePoints.Select(ZoneCharsetRows.Raw)))
            {
                rows.Add(address, Zone);
            }

            foreach (var address in ZoneCharsetRows.PunctuationOnlyHosts)
            {
                rows.Add(address, Punctuation);
            }

            foreach (var address in new[]
                     {
                         "rtsp://[fe80::1%25eth0\r\nX]/s", "rtsp://[fe80::1%25 x]/s", "rtsp://[fe80::1%25%0D@x]/s",
                         "rtsp://[::1%25x?y]/s", "rtsp://[fe80::1%25eth0]%0D/s", "rtsp://.../x", "rtsp://.cam/x",
                         "rtsp://..:554/x", "rtsp://‎/p",
                     })
            {
                rows.Add(address, Older);
            }

            return rows;
        }
    }

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

    [Fact]
    public void The_corpus_holds_every_outcome_class_row_by_row()
    {
        var rows = Corpus.Cast<object[]>().Select(row => ((string)row[0], (string)row[1])).ToList();

        rows.Count.ShouldBe(11 + 33 + 10 + 9);
        rows.Select(row => row.Item1).Distinct(StringComparer.Ordinal).Count().ShouldBe(rows.Count);
        rows.Count(row => row.Item2 == Accepted).ShouldBe(11);
        rows.Count(row => row.Item2 == Zone).ShouldBe(33);
        rows.Count(row => row.Item2 == Punctuation).ShouldBe(10);
        rows.Count(row => row.Item2 == Older).ShouldBe(9);
    }

    private static string Classify(string outcome) => outcome switch
    {
        _ when outcome == "refused: " + ZoneRefusal => Zone,
        _ when outcome == "refused: " + PunctuationRefusal => Punctuation,
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
