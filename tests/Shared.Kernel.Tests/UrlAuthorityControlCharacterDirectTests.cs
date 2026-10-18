namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 408 / issue #3031, direct rows (T060). <see cref="UrlAuthority.ContainsControlOrWhiteSpace"/> and
/// <see cref="UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText"/> are shared by the value objects and
/// <c>UrlRedaction</c>; asserted here so a change to the set is seen directly and not only through its callers.
/// Characterisation of current behaviour.
/// </summary>
public class UrlAuthorityControlCharacterDirectTests
{
    private const int MembersOfTheSet = 84;

    private static List<char> Members()
    {
        List<char> members = [];

        for (var code = (int)char.MinValue; code <= char.MaxValue; code++)
        {
            var character = (char)code;

            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                members.Add(character);
            }
        }

        return members;
    }

    [Fact]
    public void The_set_is_exactly_84_utf16_code_units()
    {
        Members().Count.ShouldBe(MembersOfTheSet);
    }

    [Fact]
    public void Every_code_unit_agrees_with_the_predicate_in_both_methods()
    {
        for (var code = (int)char.MinValue; code <= char.MaxValue; code++)
        {
            var character = (char)code;
            var expected = char.IsControl(character) || char.IsWhiteSpace(character);
            var interior = $"a{character}b";

            UrlAuthority.ContainsControlOrWhiteSpace(character.ToString()).ShouldBe(expected, $"U+{code:X4}");
            UrlAuthority.ContainsControlOrWhiteSpace(interior).ShouldBe(expected, $"U+{code:X4}");
            UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(interior).ShouldBe(expected, $"U+{code:X4}");
        }
    }

    [Fact]
    public void An_empty_text_contains_none()
    {
        UrlAuthority.ContainsControlOrWhiteSpace(string.Empty).ShouldBeFalse();
        UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(string.Empty).ShouldBeFalse();
    }

    [Fact]
    public void A_text_of_only_members_has_none_within_the_text()
    {
        var all = new string([.. Members()]);

        UrlAuthority.ContainsControlOrWhiteSpace(all).ShouldBeTrue();
        UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(all).ShouldBeFalse();
    }

    [Theory]
    [InlineData(" rtsp://cam/s")]
    [InlineData("rtsp://cam/s ")]
    [InlineData("\t\r\n rtsp://cam/s \n\r\t")]
    [InlineData("\u0085rtsp://cam/s ")]
    public void A_run_at_either_edge_alone_is_not_within_the_text(string url)
    {
        UrlAuthority.ContainsControlOrWhiteSpace(url).ShouldBeTrue();
        UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(url).ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp://ca m/s")]
    [InlineData("rtsp://cam/s\r\nHost: x")]
    [InlineData(" rtsp://ca\tm/s ")]
    [InlineData("rtsp://cam\u0000/s")]
    public void A_member_between_two_non_members_is_within_the_text(string url)
    {
        UrlAuthority.ContainsControlOrWhiteSpace(url).ShouldBeTrue();
        UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(url).ShouldBeTrue();
    }

    [Fact]
    public void A_null_text_is_rejected_by_both_methods()
    {
        Should.Throw<ArgumentNullException>(() => UrlAuthority.ContainsControlOrWhiteSpace(null!));
        Should.Throw<ArgumentNullException>(() => UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(null!));
    }

    [Theory]
    [InlineData("\uD800")]
    [InlineData("\uDC00")]
    [InlineData("rtsp://\uD800cam\uDC00/s")]
    public void A_lone_surrogate_neither_throws_nor_counts(string url)
    {
        Should.NotThrow(() => UrlAuthority.ContainsControlOrWhiteSpace(url));
        Should.NotThrow(() => UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(url));
        UrlAuthority.ContainsControlOrWhiteSpace(url).ShouldBeFalse();
        UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(url).ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp://ca‎m/s")]
    [InlineData("rtsp://ca﻿m/s")]
    [InlineData("‎rtsp://cam﻿")]
    public void A_format_character_is_not_a_member(string url)
    {
        url.Any(char.IsControl).ShouldBeFalse();
        url.Any(char.IsWhiteSpace).ShouldBeFalse();
        UrlAuthority.ContainsControlOrWhiteSpace(url).ShouldBeFalse();
        UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(url).ShouldBeFalse();
    }
}
