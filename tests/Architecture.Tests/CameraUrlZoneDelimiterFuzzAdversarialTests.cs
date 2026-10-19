using System.Net;
using System.Net.Sockets;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 394 / issue #3023 items 1, 2 and 5, adversarial, seeded fuzz. Three generators build bracketed camera
/// addresses: one from tokens (zones, delimiters, percent-encodings, whitespace, control, zero-width and fullwidth
/// characters, userinfo prefixes, ports, tails), one from a raw IPv6 alphabet, and one from structured groups with a
/// trailing ':', a prefix length and a zone in either order, the shape that makes <see cref="Uri.Host"/> throw. Over
/// every input: the two value objects agree and refuse only with <see cref="ArgumentException"/>; <c>FromStored</c>,
/// <see cref="UrlRedaction.ToOrigin"/> and the audit serialiser never throw. Over every accepted input: the zone the
/// parser keeps holds no raw '@', '?' or '#'; Go <c>net/url</c> (MediaMTX), ffmpeg <c>av_url_split</c> and a
/// first-'@' reader either fail or read the same host and port as <see cref="Uri"/>; a rendered origin names that
/// host. The corpus counts are asserted too, so a generator that stops reaching the shapes cannot pass vacuously.
/// </summary>
public class CameraUrlZoneDelimiterFuzzAdversarialTests
{
    private const int InputsPerGenerator = 50_000;

    private const string IPv6Alphabet = "0123456789abcdefABCDEF::::::....///%%%25@?#]x";

    private static readonly string[] Tokens =
    [
        "fe80::1", "::1", "1:2:3:4:5:6:7:8", "::ffff:10.0.0.1", "%25", "%", "eth0", "@", "?", "#", "/", "64", "\\", ":",
        ".", "]", "[", "x", "evil.com", "10.0.0.66:8554", " ", "\t", Character(0x200B), "%40", "%3F", "%23", "%5D",
        "%2F", "FE80::1", "0", "\0", Character(0xFF20), Character(0xFF1F), Character(0xFF03), "%2540", "%253F", "::",
        "1", "a", "%0A", "\r\n", Character(0x00A0), "@@", Character(0x0130), "%5B", "%5C",
    ];

    private static readonly string[] Schemes = ["rtsp://", "rtsp://", "rtsp://", "RTSP://", "rtsp:///", "rtsp:\\\\", "rtsp:"];

    private static readonly string[] Prefixes = ["", "", "", "@", "u@", "[]@", "@@", " ", "\t", "%40@", "[x]@", ":@", "u:p@"];

    private static readonly string[] Suffixes = ["", "", ":554", "x", "@x", ":554@x", "]", "@", ":", "%25eth0", "@evil.com", "?", "#"];

    private static readonly string[] Tails = ["", "/s", "?q", "#f", "/a@b", "/s?x#y", "/", "\\s", "?@", "#@"];

    private static readonly string[] Zones = ["", "%25eth0", "%eth0", "%251", "%", "%25", "%25@x", "%25?x", "%25#x", "%25a/b"];

    private static readonly string[] PrefixLengths = ["", "/64", "/", "/0", "/129", "/6x", "//64"];

    public static TheoryData<string, int> Corpora => new()
    {
        { "tokens", 3023 },
        { "tokens", 1 },
        { "alphabet", 3023 },
        { "structured", 3023 },
        { "structured", 11 },
        { "tokens", 3031 },
    };

    [Theory]
    [MemberData(nameof(Corpora))]
    public void No_input_escapes_as_anything_but_a_typed_refusal_and_no_accepted_input_reads_as_another_host(
        string generator,
        int seed)
    {
        var inputs = Generate(generator, seed);
        List<string> defects = [];

        foreach (var url in inputs)
        {
            Inspect(url, defects);
        }

        defects.ShouldBeEmpty(string.Join(Environment.NewLine, defects.Take(20)));
    }

    [Fact]
    public void The_corpora_reach_the_shapes_the_fix_is_about()
    {
        List<string> corpus = [.. Corpora.Cast<object[]>().SelectMany(row => Generate((string)row[0], (int)row[1]))];

        // Measured on the six seeds at 50,000 inputs each: 529 accepted (458 once spec 414 refuses an empty port),
        // 77 refused for a delimiter inside the brackets, 168 whose Uri.Host throws, 23 refused as unreadable. The
        // floors leave room for a parser change.
        List<string> refusals = [.. corpus.Select(Refusal)];

        refusals.Count(refusal => refusal.Length == 0).ShouldBeGreaterThanOrEqualTo(400);
        refusals.Count(refusal => refusal.Contains("must not have '@', '?' or '#' inside the brackets", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(60);
        corpus.Count(HostReadThrows).ShouldBeGreaterThanOrEqualTo(150);
        refusals.Count(refusal => refusal.Contains("must name a host the parser can read", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(20);
    }

    private static void Inspect(string url, List<string> defects)
    {
        var rtsp = Outcome(() => RtspUrl.From(url).Value);
        var stream = Outcome(() => StreamSourceUrl.From(url).Value);

        if (rtsp != stream)
        {
            defects.Add($"value objects disagree ({rtsp} / {stream}): {Escaped(url)}");
        }

        foreach (var (name, outcome) in new[]
                 {
                     ("RtspUrl.From", rtsp),
                     ("RtspUrl.FromStored", Outcome(() => RtspUrl.FromStored(url).Value)),
                     ("StreamSourceUrl.FromStored", Outcome(() => StreamSourceUrl.FromStored(url).Value)),
                 })
        {
            if (outcome.StartsWith("threw", StringComparison.Ordinal))
            {
                defects.Add($"{name} {outcome}: {Escaped(url)}");
            }
        }

        var origin = Outcome(() => UrlRedaction.ToOrigin(url));
        var audit = Outcome(() => AuditPayloadRedaction.Serialize(Change("rtsp://[fe80::1]/s", url)));
        var auditPrevious = Outcome(() => AuditPayloadRedaction.Serialize(Change(url, "rtsp://cam/s")));

        foreach (var (name, outcome) in new[] { ("ToOrigin", origin), ("Serialize", audit), ("Serialize(previous)", auditPrevious) })
        {
            if (outcome.StartsWith("threw", StringComparison.Ordinal) || outcome.StartsWith("refused", StringComparison.Ordinal))
            {
                defects.Add($"{name} {outcome}: {Escaped(url)}");
            }
        }

        if (rtsp != "accepted")
        {
            return;
        }

        var uri = new Uri(url);

        if (uri.HostNameType == UriHostNameType.IPv6 && uri.DnsSafeHost.AsSpan().IndexOfAny("@?#") >= 0)
        {
            defects.Add($"accepted with a delimiter in the zone the parser keeps ({Escaped(uri.DnsSafeHost)}): {Escaped(url)}");
        }

        var expected = (Normalised(uri.Host), uri.Port);

        foreach (var (reader, reading) in Readings(url))
        {
            if (reading is { } read && (Normalised(read.Host), read.Port ?? -1) != expected)
            {
                defects.Add($"{reader} reads {read.Host}:{read.Port}, Uri reads {uri.Host}:{uri.Port}: {Escaped(url)}");
            }
        }

        var rendered = origin == "accepted" ? UrlRedaction.ToOrigin(url) : UrlRedaction.Unparseable;

        if (rendered != UrlRedaction.Unparseable && Normalised(new Uri(rendered).Host) != expected.Item1)
        {
            defects.Add($"origin {rendered} names another host than {uri.Host}: {Escaped(url)}");
        }
    }

    private static string Outcome(Func<string> attempt)
    {
        try
        {
            attempt();
            return "accepted";
        }
        catch (ArgumentException refused) when (refused.GetType() == typeof(ArgumentException))
        {
            return "refused";
        }
        catch (Exception escaped)
        {
            return "threw " + escaped.GetType().Name;
        }
    }

    // The refusal message, or empty when the address is accepted.
    private static string Refusal(string url)
    {
        try
        {
            RtspUrl.From(url);
            return string.Empty;
        }
        catch (ArgumentException refused)
        {
            return refused.Message;
        }
    }

    private static bool HostReadThrows(string url)
    {
        try
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host is null;
        }
        catch (IndexOutOfRangeException)
        {
            return true;
        }
    }

    private static CameraAddressChangedV1 Change(string previous, string url) =>
        new(
            Guid.Empty,
            "dresden",
            previous,
            url,
            DateTimeOffset.UnixEpoch,
            Guid.Empty,
            new EventMetadata(Guid.Empty, DateTimeOffset.UnixEpoch, "dresden", null));

    // The host as written, read by parsers that do not know brackets the way System.Uri does. Null means the reader
    // fails on the input (a strict parser's error, or a host that starts with '[' and so resolves nowhere), which is
    // not a different host.
    private static IEnumerable<(string Reader, (string Host, int? Port)? Reading)> Readings(string url)
    {
        var rest = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];

        var go = rest;
        go = go[..IndexOrLength(go, '#')];
        go = go[..IndexOrLength(go, '?')];
        go = go[..IndexOrLength(go, '/')];
        yield return ("Go net/url", HostAndPort(go[(go.LastIndexOf('@') + 1)..]));

        var authority = rest[..IndexOrLength(rest, '/', '?', '#')];
        yield return ("ffmpeg av_url_split", FfmpegHostAndPort(authority[(authority.LastIndexOf('@') + 1)..]));
        yield return ("first-@ reader", HostAndPort(authority[(authority.IndexOf('@') + 1)..]));
    }

    private static int IndexOrLength(string text, params char[] characters)
    {
        var index = text.IndexOfAny(characters);
        return index < 0 ? text.Length : index;
    }

    private static (string Host, int? Port)? HostAndPort(string hostPort)
    {
        if (hostPort.StartsWith('['))
        {
            var close = hostPort.IndexOf(']', StringComparison.Ordinal);

            if (close < 0)
            {
                return null;
            }

            var after = hostPort[(close + 1)..];

            if (after.Length == 0 || after == ":")
            {
                return (hostPort[..(close + 1)], null);
            }

            return after[0] == ':' && int.TryParse(after[1..], out var bracketPort) ? (hostPort[..(close + 1)], bracketPort) : null;
        }

        var colon = hostPort.LastIndexOf(':');

        if (colon < 0)
        {
            return (hostPort, null);
        }

        return int.TryParse(hostPort[(colon + 1)..], out var port) ? (hostPort[..colon], port) : null;
    }

    private static (string Host, int? Port)? FfmpegHostAndPort(string hostPort)
    {
        var close = hostPort.IndexOf(']', StringComparison.Ordinal);

        if (hostPort.StartsWith('[') && close > 0)
        {
            var host = hostPort[..(close + 1)];
            return hostPort.Length > close + 1 && hostPort[close + 1] == ':' ? (host, LeadingNumber(hostPort[(close + 2)..])) : (host, null);
        }

        var colon = hostPort.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? hostPort : hostPort[..colon];

        if (name.StartsWith('['))
        {
            return null;
        }

        return colon < 0 ? (name, null) : (name, LeadingNumber(hostPort[(colon + 1)..]));
    }

    // ffmpeg's atoi of an empty port is 0, which no connection reaches, so it is read as no port (the default) rather
    // than as another port: a failed connection, not a different host.
    private static int? LeadingNumber(string text)
    {
        var digits = text.TakeWhile(char.IsAsciiDigit).ToArray();
        return digits.Length == 0 ? null : int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Normalised(string host)
    {
        var trimmed = host.Trim();

        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        var cut = trimmed.IndexOfAny(['%', '/']);
        var address = cut < 0 ? trimmed : trimmed[..cut];

        return IPAddress.TryParse(address, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetworkV6
            ? "[" + parsed + "]"
            : trimmed.ToLowerInvariant();
    }

    internal static List<string> Generate(string generator, int seed)
    {
        var random = new Random(seed);
        HashSet<string> seen = new(StringComparer.Ordinal);

        for (var attempt = 0; attempt < InputsPerGenerator; attempt++)
        {
            var inner = generator switch
            {
                "tokens" => string.Concat(Enumerable.Range(0, random.Next(1, 6)).Select(_ => Pick(random, Tokens))),
                "alphabet" => new string([.. Enumerable.Range(0, random.Next(1, 26)).Select(_ => IPv6Alphabet[random.Next(IPv6Alphabet.Length)])]),
                _ => Structured(random),
            };

            seen.Add(Pick(random, Schemes) + Pick(random, Prefixes) + "[" + inner + "]" + Pick(random, Suffixes) + Pick(random, Tails));
        }

        return [.. seen];
    }

    private static string Structured(Random random)
    {
        List<string> groups = [.. Enumerable.Range(0, random.Next(0, 10)).Select(_ => random.Next(0, 0x10000).ToString(random.Next(2) == 0 ? "x" : "X", System.Globalization.CultureInfo.InvariantCulture))];

        if (groups.Count > 0 && random.Next(3) == 0)
        {
            groups[random.Next(groups.Count)] = string.Empty;
        }

        var address = string.Join(":", groups);
        address = random.Next(4) == 0 ? ":" + address : address;
        address = random.Next(3) == 0 ? address + ":" : address;

        if (random.Next(4) == 0)
        {
            address += (address.EndsWith(':') ? string.Empty : ":") + "10.0.0." + random.Next(0, 300);
        }

        var zone = Pick(random, Zones);
        var prefixLength = Pick(random, PrefixLengths);

        return random.Next(2) == 0 ? address + zone + prefixLength : address + prefixLength + zone;
    }

    private static string Character(int codePoint) => ((char)codePoint).ToString();

    private static string Pick(Random random, string[] choices) => choices[random.Next(choices.Length)];

    private static string Escaped(string text) =>
        string.Concat(text.Select(character => character is < ' ' or > '~' ? $"\\u{(int)character:X4}" : character.ToString()));
}
