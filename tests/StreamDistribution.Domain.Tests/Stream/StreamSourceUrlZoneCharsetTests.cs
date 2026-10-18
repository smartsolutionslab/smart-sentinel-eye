using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 411 / issue #3023 (the #3031 security-review residuals), FR-001, red. <see cref="StreamSourceUrl.From"/> refuses an
/// IPv6 zone that holds anything but printable ASCII (U+0021-U+007E), written or percent-encoded: RFC 6874 makes a
/// ZoneID <c>unreserved / pct-encoded</c>, and a reader that decodes the zone turns <c>%0D%0A</c> into a line break.
/// The message is one constant, identical in <c>StreamSourceUrl</c>, and never carries any part of the address.
/// </summary>
public class StreamSourceUrlZoneCharsetTests
{
    private const string ZoneRefusal =
        "value: must hold only printable ASCII in the zone of an IPv6 address (after the '%'), written or percent-encoded: a control character, space or invisible character there is decoded by some readers and not by others (Parameter 'value')";

    public static TheoryData<string, string> Named
    {
        get
        {
            TheoryData<string, string> rows = [];
            foreach (var (address, marker) in ZoneCharsetRows.Named)
            {
                rows.Add(address, marker);
            }

            return rows;
        }
    }

    public static TheoryData<string> Encoded => [.. ZoneCharsetRows.EncodedOctets];

    public static TheoryData<int> Raw => [.. ZoneCharsetRows.RawCodePoints];

    [Theory]
    [MemberData(nameof(Named))]
    public void From_refuses_a_named_zone_with_the_constant_message_and_echoes_none_of_it(string address, string marker)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(ZoneRefusal);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain(marker, Case.Insensitive);
        refused.ToString().ShouldNotContain(address, Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(Encoded))]
    public void From_refuses_a_percent_encoded_octet_outside_printable_ascii_in_the_zone(string octets)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(ZoneCharsetRows.Encoded(octets)), octets);

        refused.Message.ShouldBe(ZoneRefusal, octets);
    }

    [Theory]
    [MemberData(nameof(Raw))]
    public void From_refuses_a_raw_character_outside_printable_ascii_in_the_zone(int codePoint)
    {
        var refused = Should.Throw<ArgumentException>(
            () => StreamSourceUrl.From(ZoneCharsetRows.Raw(codePoint)),
            ZoneCharsetRows.Label(codePoint));

        refused.Message.ShouldBe(ZoneRefusal, ZoneCharsetRows.Label(codePoint));
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        Named.Cast<object[]>().Count().ShouldBe(7);
        Encoded.Cast<object[]>().Count().ShouldBe(15);
        Raw.Cast<object[]>().Count().ShouldBe(11);
    }
}
