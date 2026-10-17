namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 392 / issue #3012, adversarial findings (T070), red against the fix. <see cref="UrlRedaction.ToOrigin"/>
/// still renders a host the address did not name in two shapes.
/// <para>
/// An '@' after the closing ']': the parser ends the userinfo at the first '@' and moves everything after the ']'
/// into the path, but <c>UrlAuthority.TextFollowsTheClosingBracket</c> takes the last '@' before the first
/// terminator as the end of the userinfo, looks for the bracket after it, finds none and answers false. So
/// <c>rtsp://@[fe80::1]admin:4242@cam/s</c> is rendered <c>rtsp://[fe80::1]</c> although the text after the ']'
/// was dropped, exactly the defect #3012 describes.
/// </para>
/// <para>
/// A '/' inside the brackets that does not introduce a prefix length: <c>[fe80/::1]</c> is read, and rendered, as
/// <c>[fe80::]</c>, an address nobody wrote.
/// </para>
/// </summary>
public class UrlRedactionAuthorityHostAdversarialFindingTests
{
    public static TheoryData<string> AtSignAfterTheClosingBracket =>
    [
        "rtsp://@[fe80::1]admin:4242@cam/s",
        "rtsp://@[fe80::1]garbage.example@x/s",
        "rtsp://@[fe80::1]]@cam/s",
        "rtsp://@[fe80::1%25eth0]@cam/s",
        "rtsp://[fe80::1]admin:4242@",
        "rtsp://[fe80::1]x@",
        "rtsp://[fe80::1]@",
        "rtsp://user:pass@[fd00::5]x@cam/s",
    ];

    public static TheoryData<string> SlashInsideTheBracketsThatIsNoPrefixLength =>
    [
        "rtsp://[fe80/::1]/s",
        "rtsp://[fe80/::1]:554/s",
        "rtsp://[0/::1]:8554/a",
    ];

    [Theory]
    [MemberData(nameof(AtSignAfterTheClosingBracket))]
    public void Text_after_the_closing_bracket_holding_an_at_sign_is_not_rendered_as_an_origin(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(SlashInsideTheBracketsThatIsNoPrefixLength))]
    public void A_slash_inside_the_brackets_that_makes_the_parser_read_an_address_never_written_is_not_rendered(
        string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }
}
