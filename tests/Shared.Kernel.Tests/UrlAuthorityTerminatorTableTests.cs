namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 389 / issue #2999, adversarial (T050). <see cref="UrlAuthority.AtSignFollowsTheAuthority"/> is the one
/// predicate behind <c>RtspUrl.From</c>, <c>StreamSourceUrl.From</c> and <see cref="UrlRedaction.ToOrigin"/>.
/// Some of its rows cannot be observed through the value objects at all: a '\' terminator in the authority makes
/// the BCL parser refuse the address, so the value objects refuse it on the userinfo rule first, and a drop of '\'
/// from the terminator set would pass every value-object test. This table pins the predicate directly, one row
/// per terminator, one per non-terminator that looks like one, and the authority-location rules.
/// </summary>
public class UrlAuthorityTerminatorTableTests
{
    [Theory]
    [InlineData("rtsp://cam/@")]
    [InlineData("rtsp://cam?@")]
    [InlineData("rtsp://cam#@")]
    [InlineData("rtsp://cam\\@")]
    [InlineData("rtsp://admin:1\\@cam/s")]
    [InlineData("rtsp://cam/s\\admin:1@x")]
    public void Each_of_the_four_terminators_alone_puts_a_later_at_sign_after_the_authority(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("rtsp://cam;@")]
    [InlineData("rtsp://cam:@")]
    [InlineData("rtsp://cam[@")]
    [InlineData("rtsp://cam]@")]
    [InlineData("rtsp://cam%2F@")]
    [InlineData("rtsp://cam%23@")]
    [InlineData("rtsp://cam\uFF0F@")]
    [InlineData("rtsp://cam\uFF03@")]
    [InlineData("rtsp://cam\u2044@")]
    [InlineData("rtsp://cam\t@")]
    [InlineData("rtsp://cam\0@")]
    [InlineData("rtsp://cam @")]
    public void A_character_that_only_resembles_a_terminator_does_not_end_the_authority(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp://admin:1#\uFF20cam")]
    [InlineData("rtsp://cam/%40x")]
    [InlineData("rtsp://cam/s?mail=a%40b")]
    [InlineData("rtsp://cam/s#%40")]
    public void Only_a_raw_ascii_at_sign_counts(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp://a@b/c@d")]
    [InlineData("rtsp://admin:1#@x@cam/s")]
    [InlineData("rtsp://cam/a@b@c")]
    public void Any_at_sign_after_the_first_terminator_counts_even_beside_one_before_it(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("rtsp:///admin:1#@cam")]
    [InlineData("rtsp:////admin:1#@cam")]
    [InlineData("rtsp:\\\\admin:1#@cam")]
    [InlineData("rtsp:/\\admin:1#@cam")]
    [InlineData("rtsp:admin:1#@cam")]
    [InlineData("RtSp://admin:1#@cam")]
    [InlineData("//admin:1#@cam")]
    [InlineData("admin:1#@cam")]
    public void The_authority_is_found_past_the_scheme_and_any_run_of_slashes_and_backslashes(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("rtsp:///admin:1@cam/s")]
    [InlineData("rtsp://\\admin:1@cam/s")]
    [InlineData("rtsp://[::1]x@cam")]
    [InlineData("rtsp://[fd00::1]:4711@cam/s")]
    public void An_at_sign_before_the_first_terminator_is_userinfo_to_the_predicate(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp:")]
    [InlineData("rtsp://")]
    [InlineData("rtsp:///")]
    [InlineData("@")]
    [InlineData("/@")]
    [InlineData("\\\\@")]
    public void An_address_with_no_authority_text_is_not_flagged(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeFalse();
    }

    [Fact]
    public void A_terminator_at_the_very_end_with_nothing_after_it_is_not_flagged()
    {
        UrlAuthority.AtSignFollowsTheAuthority("rtsp://cam/").ShouldBeFalse();
        UrlAuthority.AtSignFollowsTheAuthority("rtsp://cam\\").ShouldBeFalse();
    }

    [Fact]
    public void An_at_sign_as_the_last_of_2048_characters_is_still_seen()
    {
        var url = "rtsp://cam/" + new string('a', 2048 - "rtsp://cam/".Length - 1) + "@";

        url.Length.ShouldBe(2048);
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeTrue();
    }
}
