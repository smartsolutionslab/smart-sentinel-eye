using System.Diagnostics;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 392 / issue #3012, adversarial (T070). <see cref="UrlRedaction.ToOrigin"/> runs on untrusted text in the
/// audit consumer, so neither it nor the scans it calls may throw or take long on hostile input, a bracket tail
/// made of whitespace, control, zero-width, fullwidth or percent-encoded characters must be withheld as well as a
/// plain one, and an origin that was rendered before the change and names exactly what was written must still be.
/// </summary>
public class UrlRedactionAuthorityHostAdversarialTests
{
    private static readonly string[] Heads =
    [
        "rtsp://", "RTSP://", "file://", "file:", "\\\\", "//", "rtsp:\\\\", "http://", "x:", "rtsp:", "file:////",
        "rtsp:///", "rtsp://\\\\", string.Empty, "a+b-c.d:",
    ];

    private static readonly char[] Alphabet =
    [
        '[', ']', '@', ':', '/', '\\', '?', '#', '%', '2', '5', 'x', ' ', '\t', '\n', '\u00A0', '\u200B', '\u200E',
        '\u200F', '\u202E', '\uFF3D', '\uFF20', '0', '.', '-', '\u0001', '\u007F', '\uFEFF', 'A', ';', '"', '\u3000',
        'f', 'e', '8', '1', '\uD800', '\uDC00', '\u0130', '\u212A', '\u00DF', '\u0000', '|', '~', '\u0085', '\u2028',
    ];

    public static TheoryData<string> DegenerateInputs =>
    [
        string.Empty, "[", "]", "@", ":", "::", "a:", "@[", "[]]", "]@[", "rtsp://", "rtsp://[", "rtsp://]",
        "rtsp://@[", "rtsp://@]", "rtsp://[]", "rtsp://[]]", "rtsp://[@]", "rtsp://[fe80::1", "rtsp://@", "rtsp://@@",
        "rtsp://[fe80::1]:", "rtsp://[fe80::1]::", "rtsp://[fe80::1]@", "rtsp://[fe80::1]]@", "\\\\[", "file:[",
        "rtsp://\u0000", "rtsp://[\uD800]", "rtsp://[fe80::1]\uDC00", "rtsp://[fe80::1\u200E]x/s",
        "file://[fe80::1\u200E]x/s", "\\\\[fe80::1]\u200Ex",
    ];

    public static TheoryData<string> TailsMadeOfOddCharacters =>
    [
        "rtsp://[fe80::1]\t/x",
        "rtsp://[fe80::1] /x",
        "rtsp://[fe80::1]\r/x",
        "rtsp://[fe80::1]\n/x",
        "rtsp://[fe80::1]\u00A0/x",
        "rtsp://[fe80::1]\u200B/x",
        "rtsp://[fe80::1]\uFEFF/x",
        "rtsp://[fe80::1]\u0000/x",
        "rtsp://[fe80::1]\u007F/x",
        "rtsp://[fe80::1]\u2028/x",
        "rtsp://[fe80::1]\uFF3D/x",
        "rtsp://[fe80::1]\uFF0Fx",
        "rtsp://[fe80::1]\uFF20cam/x",
        "rtsp://[fe80::1]%2Fx",
        "rtsp://[fe80::1]%40cam/x",
        "rtsp://[fe80::1]%5D/x",
        "rtsp://[fe80::1][fe80::2]/s",
        "rtsp://[fe80::1%25eth]0]/x",
        "rtsp://@[fe80::1]x/s",
        "rtsp://user:pass@[fd00::5]x/s",
        "rtsp://user:pass@[fd00::5]x:8554/s",
        "rtsp://[fe80::1]:554 ",
        "rtsp:/\\[fe80::1]x/s",
        "file:////[fe80::1]x/s",
        "\\\\[fe80::1]x\\s",
    ];

    public static TheoryData<string, string> OriginsThatNameWhatWasWritten => new()
    {
        { "rtsp://[::1]:8554/a", "rtsp://[::1]:8554" },
        { "rtsp://host:554/[x]", "rtsp://host:554" },
        { "rtsp://cam/p]q", "rtsp://cam" },
        { "rtsp://[FE80::ABCD]:8554/s", "rtsp://[fe80::abcd]:8554" },
        { "RTSP://[fe80::1]/x", "rtsp://[fe80::1]" },
        { "rtsp://[::ffff:10.0.0.1]:554/x", "rtsp://[::ffff:10.0.0.1]:554" },
        { "rtsp://@[fe80::1]:554/s", "rtsp://[fe80::1]:554" },
        { "rtsp://[fe80::1]:", "rtsp://[fe80::1]" },
        { "rtsp://[::1]?a]b", "rtsp://[::1]" },
        { "rtsp://[::1]#]", "rtsp://[::1]" },
        { "rtsp://[fe80::1]/%40", "rtsp://[fe80::1]" },
        { "rtsp://user:pass@[fd00::5]:8554/x]y", "rtsp://[fd00::5]:8554" },
        { "rtsp://[x]@[fe80::1]/s", "rtsp://[fe80::1]" },
    };

    [Fact]
    public void No_hostile_input_makes_the_origin_or_the_authority_scans_throw()
    {
        var random = new Random(3012);
        List<string> failures = [];

        for (var index = 0; index < 300_000; index++)
        {
            var length = random.Next(0, 24);
            var characters = new char[length];
            for (var position = 0; position < length; position++)
            {
                characters[position] = Alphabet[random.Next(Alphabet.Length)];
            }

            var input = Heads[random.Next(Heads.Length)] + new string(characters);

            try
            {
                _ = UrlRedaction.ToOrigin(input);
                _ = UrlAuthority.TextFollowsTheClosingBracket(input);
                _ = UrlAuthority.AtSignFollowsTheAuthority(input);
                _ = UrlAuthority.TextFromTheAuthority(input).Length;
            }
            catch (Exception exception) when (failures.Count < 10)
            {
                failures.Add($"{exception.GetType().Name}: {Show(input)}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(DegenerateInputs))]
    public void A_degenerate_input_is_rendered_without_throwing(string input)
    {
        Should.NotThrow(() => UrlRedaction.ToOrigin(input));
        Should.NotThrow(() => UrlAuthority.TextFollowsTheClosingBracket(input));
        Should.NotThrow(() => UrlAuthority.AtSignFollowsTheAuthority(input));
    }

    [Theory]
    [InlineData(']', UrlRedaction.Unparseable)]
    [InlineData('@', UrlRedaction.Unparseable)]
    [InlineData('[', UrlRedaction.Unparseable)]
    [InlineData('/', "rtsp://[fe80::1]")]
    [InlineData('x', UrlRedaction.Unparseable)]
    public void A_hundred_thousand_character_input_is_rendered_quickly(char filler, string expected)
    {
        var input = "rtsp://[fe80::1]" + new string(filler, 100_000);
        var watch = Stopwatch.StartNew();

        var origin = UrlRedaction.ToOrigin(input);

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        origin.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(TailsMadeOfOddCharacters))]
    public void A_bracket_tail_made_of_odd_characters_is_withheld_like_a_plain_one(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(OriginsThatNameWhatWasWritten))]
    public void An_origin_that_names_exactly_what_was_written_is_still_rendered(string url, string expected)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(expected);
    }

    private static string Show(string text) =>
        string.Concat(text.Select(character => character is < ' ' or > '~'
            ? $"\\u{(int)character:X4}"
            : character.ToString()));
}
