using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Characterisation (spec 421 / issue #3023, ADR-0144), captured green on <c>cabe008a</c> before
/// <see cref="RtspUrl.From"/> learns to refuse a plain host holding a non-ASCII character. The addresses of spec 421 §6
/// that stay accepted (A-labels, ASCII punctuation, and non-ASCII in a path, query or fragment, which Q2 leaves
/// alone), and the rows an older rule refuses with its own message, which the new rule (placed last) must not take
/// over. None of these rows may be edited by the change.
/// </summary>
public class RtspUrlNonAsciiHostCharacterisationTests
{
    public static TheoryData<string, string> Accepted
    {
        get
        {
            TheoryData<string, string> rows = [];
            foreach (var (label, address) in NonAsciiHostRows.Accepted)
            {
                rows.Add(label, address);
            }

            return rows;
        }
    }

    public static TheoryData<string, string, string> RefusedOnAnOlderRule
    {
        get
        {
            TheoryData<string, string, string> rows = [];
            foreach (var (label, address, refusal) in NonAsciiHostRows.OlderRule)
            {
                rows.Add(label, address, refusal);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void From_keeps_accepting_the_address_and_keeps_it_verbatim(string label, string address)
    {
        RtspUrl.From(address).Value.ShouldBe(address, label);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void FromStored_keeps_reading_the_same_address_back(string label, string address)
    {
        RtspUrl.FromStored(address).Value.ShouldBe(address, label);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRule))]
    public void From_keeps_refusing_with_the_older_rule_s_message(string label, string address, string refusal)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address), label);

        refused.Message.ShouldBe(refusal, label);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        Accepted.Cast<object[]>().Count().ShouldBe(13);
        RefusedOnAnOlderRule.Cast<object[]>().Count().ShouldBe(21);
    }
}
