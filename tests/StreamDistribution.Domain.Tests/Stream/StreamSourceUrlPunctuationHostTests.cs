using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 411 / issue #3023 item 7, FR-002, red. <see cref="StreamSourceUrl.From"/> refuses a host made only of '.', '-' or
/// '_': it is never resolvable, so a camera registered at it can never connect. A fully qualified name with a
/// trailing dot (<c>cam.</c>) names a machine and stays accepted (characterised in
/// <see cref="StreamSourceUrlZoneCharsetCharacterisationTests"/>).
/// </summary>
public class StreamSourceUrlPunctuationHostTests
{
    private const string PunctuationRefusal =
        "value: must name a host holding a letter or digit: a host of only '.', '-' or '_' names no machine (Parameter 'value')";

    public static TheoryData<string> Hosts => [.. ZoneCharsetRows.PunctuationOnlyHosts];

    [Theory]
    [MemberData(nameof(Hosts))]
    public void From_refuses_a_host_of_only_dots_hyphens_or_underscores_with_the_constant_message(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(PunctuationRefusal);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain(address, Case.Insensitive);
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        Hosts.Cast<object[]>().Count().ShouldBe(10);
    }
}
