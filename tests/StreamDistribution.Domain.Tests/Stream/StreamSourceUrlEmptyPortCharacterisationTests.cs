using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Characterisation (spec 414 / issue #3031 item 3, ADR-0144), captured green on <c>1cbae37d</c> before
/// <see cref="StreamSourceUrl.From"/> learns to refuse a ':' that names no port. The addresses of spec 414 §6 that stay
/// accepted, including an explicit port 0 and a ':' in the path, query or fragment, and the rows an older rule
/// refuses with its own message, which the new rule (placed last) must not take over. None of these rows may be
/// edited by the change.
/// </summary>
public class StreamSourceUrlEmptyPortCharacterisationTests
{
    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private const string EmptyHostRefusal =
        "value: must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    private const string UnreadableHostRefusal =
        "value: must name a host the parser can read: the address inside the brackets is malformed, for example a ':' left before a '/' prefix length (Parameter 'value')";

    private const string DelimiterRefusal =
        "value: must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host (Parameter 'value')";

    private const string ControlOrWhiteSpaceRefusal =
        "value: must not contain a control character or whitespace anywhere: a line break, tab or space is copied into the request a reader sends. Percent-encode a space in a path or query as %20 (Parameter 'value')";

    private const string NonPrintableZoneRefusal =
        "value: must hold only printable ASCII in the zone of an IPv6 address (after the '%'), written or percent-encoded: a control character, space or invisible character there is decoded by some readers and not by others (Parameter 'value')";

    private const string PunctuationOnlyHostRefusal =
        "value: must name a host holding a letter or digit: a host of only '.', '-' or '_' names no machine (Parameter 'value')";

    public static TheoryData<string> Accepted => [.. EmptyPortRows.Accepted];

    public static TheoryData<string, string> RefusedOnAnOlderRule => new()
    {
        { "rtsp://cam::/x", UserInfoRefusal },
        { "rtsp://[::1]::/x", UserInfoRefusal },
        { "rtsp://u@cam:/x", UserInfoRefusal },
        { "rtsp://..:/x", UserInfoRefusal },
        { "rtsp:///cam:/x", EmptyHostRefusal },
        { "rtsp://[fe80::1]x:/s", BracketTailRefusal },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]:/s", UnreadableHostRefusal },
        { "rtsp://[::1%25x?y]:/s", DelimiterRefusal },
        { "rtsp://cam: ", ControlOrWhiteSpaceRefusal },
        { "rtsp://[fe80::1%25eth0%0D]:/s", NonPrintableZoneRefusal },
        { "rtsp://-:/x", PunctuationOnlyHostRefusal },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void From_keeps_accepting_the_address_and_keeps_it_verbatim(string address)
    {
        StreamSourceUrl.From(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void FromStored_keeps_reading_the_same_address_back(string address)
    {
        StreamSourceUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRule))]
    public void From_keeps_refusing_with_the_older_rule_s_message(string address, string refusal)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(refusal);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        Accepted.Cast<object[]>().Count().ShouldBe(15);
        RefusedOnAnOlderRule.Cast<object[]>().Count().ShouldBe(11);
        EmptyPortRows.OlderRule.Length.ShouldBe(11);
    }
}
