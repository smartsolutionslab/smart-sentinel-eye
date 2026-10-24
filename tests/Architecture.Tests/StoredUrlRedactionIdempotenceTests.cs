using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 449 (#2990), P1, ADR-0184 Implementation Note 2. The in-place rewrite of stored camera URLs re-applies
/// <see cref="UrlRedaction.ToOrigin"/> to values the capture handler may already have reduced, and a second run
/// re-reads rows the first run wrote; both are safe only if <c>ToOrigin(ToOrigin(x)) == ToOrigin(x)</c> for every
/// input. Expected green on unchanged code: this is a property of existing behaviour, not new behaviour. The
/// inputs are the seeded corpora of <see cref="CameraUrlZoneDelimiterFuzzAdversarialTests"/> (zones, delimiters,
/// non-ASCII, userinfo, bracket tails, control characters) plus the placeholder and plain origins.
/// </summary>
public class StoredUrlRedactionIdempotenceTests
{
    private static readonly string[] Handwritten =
    [
        UrlRedaction.Unparseable,
        "rtsp://cam-a.test",
        "rtsp://cam-a.test:8554",
        "rtsp://[fe80::1]:8554",
        "rtsp://USERPWMARK:USERPWMARK@cam-a.test:8554/p?q=SECRETMARK#f",
        "rtsp://[fe80::1%25eth0]/s",
        "rtsp://[fe80::1/64%25eth0]/s",
        "rtsp://u@[::1]@evil.test/s",
        "rtsp://c" + (char)0x00E4 + "m-a.test/s",
        "rtsp://" + (char)0xFF43 + "am-a.test/s",
        "not an address",
        string.Empty,
    ];

    [Fact]
    public void Reducing_a_reduced_value_changes_nothing_for_the_fuzz_corpora_and_the_handwritten_cases()
    {
        List<string> inputs =
        [
            .. Handwritten,
            .. CameraUrlZoneDelimiterFuzzAdversarialTests.Corpora
                .Cast<object[]>()
                .SelectMany(row => CameraUrlZoneDelimiterFuzzAdversarialTests.Generate((string)row[0], (int)row[1])),
        ];
        List<string> defects = [];

        foreach (var input in inputs)
        {
            var once = UrlRedaction.ToOrigin(input);
            var twice = UrlRedaction.ToOrigin(once);

            if (!string.Equals(once, twice, StringComparison.Ordinal))
            {
                defects.Add($"{Escaped(input)} -> {Escaped(once)} -> {Escaped(twice)}");
            }
        }

        inputs.Count.ShouldBeGreaterThan(100_000, "the property must have been checked over the corpora, not vacuously");
        defects.ShouldBeEmpty(string.Join(Environment.NewLine, defects.Take(20)));
    }

    private static string Escaped(string text) =>
        string.Concat(text.Select(character => character is < ' ' or > '~' ? $"{(char)92}u{(int)character:X4}" : character.ToString()));
}
