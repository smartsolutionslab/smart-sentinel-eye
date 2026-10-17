using System.Globalization;
using System.Net;
using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 392 / issues #3012 and #3017, adversarial findings (T070), red against the fix. Each case is an address
/// <see cref="RtspUrl.From"/> accepts although the host the parser reads is not the host that was written.
/// <para>
/// An '@' after the closing ']': the parser ends the userinfo at the first '@', so in
/// <c>rtsp://@[fe80::1]admin:4242@cam/s</c> the host is <c>[fe80::1]</c> and <c>admin:4242@cam</c> is moved into
/// the path. <c>UrlAuthority.TextFollowsTheClosingBracket</c> takes the last '@' before the first terminator as the
/// end of the userinfo, so it looks for the bracket at <c>cam</c>, finds none, and answers false; the raw-'@' rule
/// sees no '@' after a terminator either. A parser that, like ffmpeg, ends the userinfo at the last '@' reads host
/// <c>cam</c> with a password.
/// </para>
/// <para>
/// A '/' inside the brackets that does not introduce a prefix length: the parser reads <c>[fe80/::1]</c> as
/// <c>[fe80::]</c>, an address that was never written.
/// </para>
/// <para>
/// A host made only of a blank or invisible character passes the empty-host rule, which compares the parsed host
/// with the empty string.
/// </para>
/// </summary>
public class RtspUrlAuthorityHostAdversarialFindingTests
{
    public static TheoryData<string, string> AtSignAfterTheClosingBracket => new()
    {
        { "rtsp://@[fe80::1]admin:4242@cam/s", "4242" },
        { "rtsp://@[fe80::1]admin:4242@cam", "4242" },
        { "rtsp://@[fd00::5]x@10.0.0.9:554/s", "10.0.0.9" },
        { "rtsp://@[fe80::1]garbage.example@x/s", "garbage.example" },
        { "rtsp://@[fe80::1]]@cam/s", "cam" },
        { "rtsp://@[fe80::1]@admin:4242/s", "4242" },
        { "rtsp://@[fe80::1%25eth0]@cam/s", "eth0" },
        { "rtsp://[fe80::1]admin:4242@", "4242" },
        { "rtsp://[fe80::1]x@", "n/a" },
        { "rtsp://[::1]8554a@", "8554a" },
        { "rtsp://[fe80::1]@", "n/a" },
    };

    public static TheoryData<string, string> SlashInsideTheBracketsThatIsNoPrefixLength => new()
    {
        { "rtsp://[fe80/::1]/s", "[fe80::]" },
        { "rtsp://[fe80/::1]:554/s", "[fe80::]" },
        { "rtsp://[0/::1]:8554/a", "[::]" },
    };

    public static TheoryData<string> BlankOrInvisibleHosts =>
    [
        "rtsp://\u00A0/x",
        "rtsp://\u200B/x",
        "rtsp://\u3000",
        "rtsp://\uFEFF",
        "rtsp://\u2028/x",
        "rtsp://\u00AD/x",
    ];

    [Theory]
    [MemberData(nameof(AtSignAfterTheClosingBracket))]
    public void Text_after_the_closing_bracket_holding_an_at_sign_is_refused_and_the_refusal_carries_none_of_it(
        string address,
        string secret)
    {
        var thrown = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        if (secret != "n/a")
        {
            thrown.Message.ShouldNotContain(secret, Case.Insensitive);
        }
    }

    [Theory]
    [MemberData(nameof(SlashInsideTheBracketsThatIsNoPrefixLength))]
    public void A_slash_inside_the_brackets_that_makes_the_parser_read_an_address_never_written_is_refused(
        string address,
        string hostTheParserReads)
    {
        new Uri(address).Host.ShouldBe(hostTheParserReads);

        Should.Throw<ArgumentException>(() => RtspUrl.From(address));
    }

    [Theory]
    [MemberData(nameof(BlankOrInvisibleHosts))]
    public void A_host_that_is_only_a_blank_or_invisible_character_is_refused_as_naming_no_host(string address)
    {
        Should.Throw<ArgumentException>(() => RtspUrl.From(address));
    }

    [Fact]
    public void For_every_bracketed_address_accepted_the_host_and_port_the_parser_reads_are_the_ones_written()
    {
        var mismatches = BracketedAuthorityFuzz.Mismatches(
            address =>
            {
                try
                {
                    RtspUrl.From(address);

                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            },
            seed: 392,
            count: 150_000);

        mismatches.Count.ShouldBe(0, BracketedAuthorityFuzz.Describe(mismatches));
    }
}

/// <summary>
/// Seeded generator of bracketed rtsp addresses built from pieces that have each moved a host before (userinfo,
/// prefix length, zone, a doubled or fullwidth ']', whitespace, control, zero-width and percent-encoded characters,
/// every terminator), plus single-character mutations of accepted addresses. For each address the predicate
/// accepts, it compares what was written between the userinfo and the first terminator after the ']' with what
/// the parser read: the text after ']' must be empty or ':' and digits, the port must be the one written, and
/// the bracket text up to its '/' or '%' must be the address the parser read.
/// </summary>
internal static class BracketedAuthorityFuzz
{
    private static readonly string[] Prefixes = ["rtsp://", "RTSP://", "RtSp://"];

    private static readonly string[] Userinfos = ["", "", "", "@", "@@", "[]@", "u@", "%40", "\uFF20"];

    private static readonly string[] Opens = ["[", "[", "[", "", "\uFF3B", "%5B"];

    private static readonly string[] Addresses =
    [
        "fe80::1", "::1", "FE80::ABCD", "fe80::abcd", "::ffff:10.0.0.1", "::ffff:A00:1", "0:0:0:0:0:0:0:1", "::",
        "1.2.3.4", "v1.x", "fe80::%31", "fe80::1:2:3:4:5:6", "cam", "",
    ];

    private static readonly string[] PrefixLengths = ["", "", "/64", "/0", "/129", "/"];

    private static readonly string[] Zones =
    [
        "", "", "%25eth0", "%eth0", "%25", "%", "%2525", "%25%5D", "%25eth0%5D", "%25]", "%25a]b", "%25\u200B", "%25 ",
    ];

    private static readonly string[] Closes = ["]", "]", "]", "]]", "", "\uFF3D", "%5D"];

    private static readonly string[] Tails =
    [
        "", "", "", ":", ":554", ":0554", ":65535", ":0", ":00000000000554", "x", "%25eth0", " ", "\t", "\n", "\r",
        "\u00A0", "\u200B", "\uFEFF", "]", ":5x", "%5D", "\uFF3D", ".", "@cam", ":554 ", ":554\u200B", "[fe80::2]",
        "%20", "%2F", "%3A554", ":\uFF15\uFF15\uFF14", "\u0000", "\u3000", "x@", "admin:4242@cam",
    ];

    private static readonly string[] Terminators =
        ["", "/", "/s", "?q", "#f", "\\x", "/[x]", "/p]q", "/a?b#c", "?a]b", "#]", "/%5D"];

    private static readonly string[] Suffixes = ["", "", " ", "\t", "\n", "\u00A0"];

    private static readonly string[] Bases =
    [
        "rtsp://[fe80::1]:554/s", "rtsp://[::1]:8554/a", "rtsp://[fe80::1%25eth0]/x", "rtsp://@[fe80::1]/s",
        "RTSP://[FE80::1]", "rtsp://[::ffff:10.0.0.1]:554/x?q#f",
    ];

    private static readonly char[] Alphabet =
    [
        '[', ']', '@', ':', '/', '\\', '?', '#', '%', '2', '5', 'x', ' ', '\t', '\n', '\u00A0', '\u200B', '\uFF3D',
        '\uFF20', '0', '.', '-', '\u0001', '\u007F', '\uFEFF', 'A', ';', '"', '<', '\u3000', '\u200E',
    ];

    private static readonly char[] Terminator = ['/', '?', '#', '\\'];

    public static List<string> Mismatches(Func<string, bool> accepts, int seed, int count)
    {
        var random = new Random(seed);
        HashSet<string> seen = [];
        List<string> mismatches = [];

        for (var index = 0; index < count; index++)
        {
            var composed = Pick(random, Prefixes) + Pick(random, Userinfos) + Pick(random, Opens)
                + Pick(random, Addresses) + Pick(random, PrefixLengths) + Pick(random, Zones) + Pick(random, Closes)
                + Pick(random, Tails) + Pick(random, Terminators) + Pick(random, Suffixes);
            Inspect(composed);

            Inspect(Mutated(random));
        }

        return mismatches;

        void Inspect(string address)
        {
            if (seen.Add(address) && accepts(address) && Mismatch(address) is { } reason)
            {
                mismatches.Add(reason);
            }
        }
    }

    public static string Describe(List<string> mismatches) =>
        $"{mismatches.Count} accepted addresses whose host or port is not the one written; shortest of each kind:{Environment.NewLine}"
        + string.Join(
            Environment.NewLine,
            mismatches
                .GroupBy(reason => reason.Split(' ')[0])
                .SelectMany(kind => kind.OrderBy(reason => reason.Length).Take(4)));

    private static string? Mismatch(string address)
    {
        var uri = new Uri(address, UriKind.Absolute);
        var written = address["rtsp://".Length..];
        var terminator = written.IndexOfAny(Terminator);
        var authority = terminator < 0 ? written : written[..terminator];
        var hostText = written[(authority.IndexOf('@', StringComparison.Ordinal) + 1)..];

        if (!hostText.StartsWith('['))
        {
            return uri.HostNameType == UriHostNameType.IPv6
                ? $"userinfo: host {uri.Host} read, but the text after the first '@' does not start with '[': {Show(address)}"
                : null;
        }

        var close = hostText.IndexOf(']', StringComparison.Ordinal);

        if (close < 0)
        {
            return $"no ']' after the '[': {Show(address)}";
        }

        var afterBracket = hostText[(close + 1)..];
        var restEnd = afterBracket.IndexOfAny(Terminator);
        var rest = restEnd < 0 ? afterBracket : afterBracket[..restEnd];

        if (rest.Length > 0 && (rest[0] != ':' || !rest[1..].All(char.IsAsciiDigit)))
        {
            return $"text '{Show(rest)}' after ']' dropped, host read {uri.Host}: {Show(address)}";
        }

        var digits = rest.Length == 0 ? string.Empty : rest[1..];
        var writtenPort = digits.Length == 0 ? -1 : int.Parse(digits, CultureInfo.InvariantCulture);

        if (uri.Port != writtenPort)
        {
            return $"port {uri.Port} read, {Show(digits)} written: {Show(address)}";
        }

        var content = hostText[1..close];
        var bare = content.Split('%')[0].Split('/')[0];

        return IPAddress.TryParse(bare, out var writtenAddress)
            && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var readAddress)
            && writtenAddress.Equals(readAddress)
                ? null
                : $"host {uri.Host} read, [{Show(content)}] written: {Show(address)}";
    }

    private static string Mutated(Random random)
    {
        List<char> characters = [.. Pick(random, Bases)];
        var edits = random.Next(1, 4);

        for (var edit = 0; edit < edits; edit++)
        {
            var at = random.Next("rtsp://".Length, characters.Count + 1);

            if (random.Next(3) == 0 && at < characters.Count)
            {
                characters.RemoveAt(at);
            }
            else
            {
                characters.Insert(at, Alphabet[random.Next(Alphabet.Length)]);
            }
        }

        return new string([.. characters]);
    }

    private static string Pick(Random random, string[] pieces) => pieces[random.Next(pieces.Length)];

    private static string Show(string text) =>
        string.Concat(text.Select(character => character is < ' ' or > '~'
            ? $"\\u{(int)character:X4}"
            : character.ToString()));
}
