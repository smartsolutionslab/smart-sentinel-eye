using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 414 / issue #3031 item 3, FR-001, red. <see cref="StreamSourceUrl.From"/> refuses a ':' after the host that names no
/// port: <c>Uri</c> reads an empty port and no port alike, while ffmpeg reads port 0, so a stored address that one
/// reader dials the default port of, another cannot. The message is one constant, identical in
/// <c>RtspUrl</c>, and never carries any part of the address.
/// </summary>
public class StreamSourceUrlEmptyPortTests
{
    private const string EmptyPortRefusal =
        "value: must write a port after the ':' that follows the host, or leave the ':' out: an empty port is read as the default port by some readers and as port 0 by others (Parameter 'value')";

    public static TheoryData<string> Refused => [.. EmptyPortRows.EmptyPort];

    [Theory]
    [MemberData(nameof(Refused))]
    public void From_refuses_a_colon_after_the_host_that_names_no_port_with_the_constant_message(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address), address);

        refused.Message.ShouldBe(EmptyPortRefusal, address);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain(address, Case.Insensitive);
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        Refused.Cast<object[]>().Count().ShouldBe(24);
    }
}
