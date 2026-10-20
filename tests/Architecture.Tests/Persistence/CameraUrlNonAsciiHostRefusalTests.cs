using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 421 / issue #3023, FR-005, red. The two camera URL value objects live in two contexts and are meant to be the
/// same rule. Over one hand-tagged corpus (the union of the spec 421 §6 sets, every row tagged with the outcome class
/// the spec assigns it) this asserts, row by row and unconditionally, that both <c>From</c>s give the tagged outcome
/// and the same message, that nothing but an <see cref="ArgumentException"/> escapes either, and that an address an
/// older rule refuses keeps that rule's message because the new rule is last. The agreement and escape checks pass on
/// arrival and ride along.
/// </summary>
public class CameraUrlNonAsciiHostRefusalTests
{
    private const string Accepted = "accepted";

    private const string NonAscii = "non-ascii-host";

    private const string Older = "older-rule";

    public static TheoryData<string, string, string> Corpus
    {
        get
        {
            TheoryData<string, string, string> rows = [];
            foreach (var (label, address) in NonAsciiHostRows.Accepted)
            {
                rows.Add(label, address, Accepted);
            }

            foreach (var (label, address) in NonAsciiHostRows.NonAscii)
            {
                rows.Add(label, address, NonAscii);
            }

            foreach (var (label, address, _) in NonAsciiHostRows.OlderRule)
            {
                rows.Add(label, address, Older);
            }

            return rows;
        }
    }

    public static TheoryData<string, string> Addresses
    {
        get
        {
            TheoryData<string, string> rows = [];
            foreach (var row in Corpus.Cast<object[]>())
            {
                rows.Add((string)row[0], (string)row[1]);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Both_write_factories_give_the_tagged_outcome(string label, string address, string tag)
    {
        var camera = Classify(Outcome(() => RtspUrl.From(address).Value));
        var stream = Classify(Outcome(() => StreamSourceUrl.From(address).Value));

        camera.ShouldBe(tag, label);
        stream.ShouldBe(tag, label);
    }

    [Theory]
    [MemberData(nameof(Addresses))]
    public void The_two_write_factories_agree_on_the_outcome_and_the_message(string label, string address)
    {
        Outcome(() => StreamSourceUrl.From(address).Value).ShouldBe(Outcome(() => RtspUrl.From(address).Value), label);
    }

    [Theory]
    [MemberData(nameof(Addresses))]
    public void Nothing_but_an_ArgumentException_escapes_either_write_factory(string label, string address)
    {
        Outcome(() => RtspUrl.From(address).Value).ShouldNotStartWith("threw: ", Case.Sensitive, label);
        Outcome(() => StreamSourceUrl.From(address).Value).ShouldNotStartWith("threw: ", Case.Sensitive, label);
    }

    [Fact]
    public void An_address_an_older_rule_refuses_keeps_that_rule_s_message_in_both()
    {
        foreach (var (label, address, refusal) in NonAsciiHostRows.OlderRule)
        {
            Outcome(() => RtspUrl.From(address).Value).ShouldBe("refused: " + refusal, label);
            Outcome(() => StreamSourceUrl.From(address).Value).ShouldBe("refused: " + refusal, label);
        }
    }

    [Fact]
    public void The_corpus_holds_every_outcome_class_row_by_row()
    {
        var rows = Corpus.Cast<object[]>().Select(row => ((string)row[1], (string)row[2])).ToList();

        rows.Count.ShouldBe(13 + 38 + 21);
        rows.Select(row => row.Item1).Distinct(StringComparer.Ordinal).Count().ShouldBe(rows.Count);
        rows.Count(row => row.Item2 == Accepted).ShouldBe(13);
        rows.Count(row => row.Item2 == NonAscii).ShouldBe(38);
        rows.Count(row => row.Item2 == Older).ShouldBe(21);
    }

    private static string Classify(string outcome) => outcome switch
    {
        _ when outcome == "refused: " + NonAsciiHostRows.NonAsciiHostRefusal => NonAscii,
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
