using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 414 / issue #3031 item 3, FR-005, red. The two camera URL value objects live in two contexts and are meant to
/// be the same rule. Over one hand-tagged corpus (the union of the spec 414 §6 sets, every row tagged with the
/// outcome class the spec assigns it) this asserts, row by row and unconditionally, that both <c>From</c>s give the
/// tagged outcome and the same message, that nothing but an <see cref="ArgumentException"/> escapes either, and that
/// an address an older rule refuses keeps that rule's message because the new rule is last.
/// </summary>
public class CameraUrlEmptyPortRefusalTests
{
    private const string Accepted = "accepted";

    private const string EmptyPort = "empty-port";

    private const string Older = "older-rule";

    private const string EmptyPortRefusal =
        "value: must write a port after the ':' that follows the host, or leave the ':' out: an empty port is read as the default port by some readers and as port 0 by others (Parameter 'value')";

    public static TheoryData<string, string> Corpus
    {
        get
        {
            TheoryData<string, string> rows = [];
            foreach (var address in EmptyPortRows.Accepted)
            {
                rows.Add(address, Accepted);
            }

            foreach (var address in EmptyPortRows.EmptyPort)
            {
                rows.Add(address, EmptyPort);
            }

            foreach (var address in EmptyPortRows.OlderRule)
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

        rows.Count.ShouldBe(15 + 24 + 11);
        rows.Select(row => row.Item1).Distinct(StringComparer.Ordinal).Count().ShouldBe(rows.Count);
        rows.Count(row => row.Item2 == Accepted).ShouldBe(15);
        rows.Count(row => row.Item2 == EmptyPort).ShouldBe(24);
        rows.Count(row => row.Item2 == Older).ShouldBe(11);
    }

    private static string Classify(string outcome) => outcome switch
    {
        _ when outcome == "refused: " + EmptyPortRefusal => EmptyPort,
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
