namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 411 / issue #3023, direct rows (T060). <see cref="UrlAuthority.NonPrintableCharacterInsideTheZone"/> and
/// <see cref="UrlAuthority.IsPunctuationOnlyHost"/> are asserted here so a change to the set is seen directly and not
/// only through the value objects. Characterisation of current behaviour.
/// </summary>
public class UrlAuthorityZoneCharsetTests
{
    private static string WithZone(string zone) => $"rtsp://[fe80::1%25{zone}]/s";

    private static string WithRawZone(string zone) => $"rtsp://[fe80::1%{zone}]/s";

    [Fact]
    public void A_null_url_is_refused_by_the_zone_predicate()
    {
        Should.Throw<ArgumentNullException>(() => UrlAuthority.NonPrintableCharacterInsideTheZone(null!));
    }

    [Fact]
    public void A_null_host_is_refused_by_the_punctuation_predicate()
    {
        Should.Throw<ArgumentNullException>(() => UrlAuthority.IsPunctuationOnlyHost(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("rtsp://cam/s")]
    [InlineData("rtsp://[fe80::1]/s")]
    [InlineData("rtsp://[fe80::1/s")]
    [InlineData("rtsp://cam/s%00")]
    public void A_text_with_no_zone_has_no_non_printable_character(string url)
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone(url).ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp://[fe80::1%25eth0]/s")]
    [InlineData("rtsp://[fe80::1/64%25eth0]/s")]
    [InlineData("rtsp://[fe80::1%eth0]/s")]
    [InlineData("rtsp://u:p@[fe80::1%25eth0]:554/s")]
    [InlineData("rtsp://a@b@[fe80::1%25eth0]/s")]
    public void A_zone_of_printable_ascii_is_accepted(string url)
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone(url).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_zone_is_accepted()
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone("rtsp://[fe80::1%25]/s").ShouldBeFalse();
        UrlAuthority.NonPrintableCharacterInsideTheZone("rtsp://[fe80::1%]/s").ShouldBeFalse();
    }

    [Theory]
    [InlineData('\u0000')]
    [InlineData(' ')]
    [InlineData('\u007F')]
    [InlineData('\u0085')]
    [InlineData(' ')]
    [InlineData('‎')]
    [InlineData('ㅤ')]
    [InlineData('⠀')]
    [InlineData('﻿')]
    public void A_raw_non_printable_character_in_the_zone_is_found(char character)
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone($"eth{character}0")).ShouldBeTrue();
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithRawZone($"eth{character}0")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("%00")]
    [InlineData("%0D%0A")]
    [InlineData("%20")]
    [InlineData("%7F")]
    [InlineData("%C2%85")]
    [InlineData("%E2%80%8E")]
    [InlineData("%0d")]
    public void A_percent_encoded_non_printable_octet_in_the_zone_is_found(string encoded)
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone($"eth0{encoded}")).ShouldBeTrue();
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone($"{encoded}eth0")).ShouldBeTrue();
    }

    [Fact]
    public void Exactly_the_octets_0x21_to_0x7E_are_accepted_when_percent_encoded()
    {
        for (var octet = 0; octet <= 0xFF; octet++)
        {
            var expected = octet is < 0x21 or > 0x7E;

            UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone($"eth0%{octet:X2}")).ShouldBe(expected, $"%{octet:X2}");
        }
    }

    [Fact]
    public void Exactly_the_code_units_0x21_to_0x7E_are_accepted_raw()
    {
        for (var code = 0; code <= 0xFF; code++)
        {
            var character = (char)code;

            if (character is '%' or ']' or '[')
            {
                continue;
            }

            var expected = code is < 0x21 or > 0x7E;

            UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone($"eth{character}0")).ShouldBe(expected, $"U+{code:X4}");
        }
    }

    [Theory]
    [InlineData("%")]
    [InlineData("%4")]
    [InlineData("%G1")]
    [InlineData("%4G")]
    [InlineData("%%41")]
    public void A_percent_not_followed_by_two_hex_digits_is_one_printable_code_unit(string zone)
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone($"eth0{zone}")).ShouldBeFalse();
    }

    [Fact]
    public void A_malformed_percent_does_not_swallow_the_character_after_it()
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone("eth0%4\u0000")).ShouldBeTrue();
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone("eth0% ")).ShouldBeTrue();
    }

    [Fact]
    public void An_octet_that_ends_the_zone_is_decoded()
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone("%00")).ShouldBeTrue();
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone("%41")).ShouldBeFalse();
    }

    [Fact]
    public void Lone_surrogates_in_the_zone_do_not_throw_and_are_non_printable()
    {
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone("eth\uD800")).ShouldBeTrue();
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone("eth\uDC00")).ShouldBeTrue();
        UrlAuthority.NonPrintableCharacterInsideTheZone(WithZone("eth😀")).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("-")]
    [InlineData("_")]
    [InlineData("._-")]
    [InlineData("-._")]
    public void A_host_of_only_dot_hyphen_and_underscore_is_punctuation_only(string host)
    {
        UrlAuthority.IsPunctuationOnlyHost(host).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("cam.")]
    [InlineData("cam_1")]
    [InlineData("a-b")]
    [InlineData("0")]
    [InlineData("a")]
    [InlineData(".a")]
    [InlineData(" ")]
    [InlineData("ä")]
    public void A_host_with_anything_else_is_not_punctuation_only(string host)
    {
        UrlAuthority.IsPunctuationOnlyHost(host).ShouldBeFalse();
    }

    [Fact]
    public void Neither_predicate_throws_on_arbitrary_text()
    {
        var random = new Random(411);
        var alphabet = "[]%@/:?#.-_ 0123456789abcdefABCDEFG25\u0000\r\n\u007F\u0085 ‎𐀀﻿";

        for (var round = 0; round < 2000; round++)
        {
            var text = new string([.. Enumerable.Range(0, random.Next(0, 24)).Select(_ => alphabet[random.Next(alphabet.Length)])]);
            var url = random.Next(2) == 0 ? text : "rtsp://" + text;

            Should.NotThrow(() => UrlAuthority.NonPrintableCharacterInsideTheZone(url), url);
            Should.NotThrow(() => UrlAuthority.IsPunctuationOnlyHost(text), text);
        }
    }
}
