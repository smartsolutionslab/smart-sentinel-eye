using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 421 / issue #3023 (owner decision Q1), FR-001, red. <see cref="RtspUrl.From"/> refuses a plain host that holds
/// any non-ASCII character: the parser drops or folds some of them, so the stored text is not the host every reader
/// dials. The message is one constant, identical in the other camera URL value object, and never carries any part of
/// the address. An A-label (<c>xn--</c>) host, ASCII punctuation and non-ASCII in a path stay accepted (pinned in the
/// characterisation tests).
/// </summary>
public class RtspUrlNonAsciiHostTests
{
    private static readonly string[] HostFragments = ["münchen", "SECRET", "bücher", "kamera", "cam", "10.0.0"];

    public static TheoryData<string, string> Refused
    {
        get
        {
            TheoryData<string, string> rows = [];
            foreach (var (label, address) in NonAsciiHostRows.NonAscii)
            {
                rows.Add(label, address);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void From_refuses_a_plain_host_holding_a_non_ascii_character_with_the_constant_message(string label, string address)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address), label);

        refused.Message.ShouldBe(NonAsciiHostRows.NonAsciiHostRefusal, label);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain(address, Case.Insensitive);
        foreach (var fragment in HostFragments)
        {
            refused.Message.ShouldNotContain(fragment, Case.Insensitive);
        }
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        Refused.Cast<object[]>().Count().ShouldBe(38);
    }
}
