using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 416 / issue #3023 item 6. <see cref="UrlAuthority.BracketContentIsAddressOnly"/> is public, so a future
/// caller sees it on every input and not only on those the audit reaches. It must answer exactly as
/// <see cref="AuditPayloadBracketScanOracle"/> (a frozen copy of the audit's scan at <c>d24ee84e</c>) on the whole
/// corpus: the six fuzz seeds plus explicit rows, with no reach filter.
/// </summary>
public class UrlAuthorityBracketContentOracleEquivalenceTests
{
    // Measured on the six seeds plus the explicit rows at the fold: accepted=11033, refused=288080.
    // Each floor is about 90% of the measurement, so an always-true or always-false member cannot pass.
    private const int TrueFloor = 9900;
    private const int FalseFloor = 259000;

    private static readonly string[] ExplicitRows =
    [
        "rtsp://[]@[FE80::1]",
        "rtsp://[x]@[::1]:/s?x#y",
        "rtsp://a@b@[fe80::1%25eth0]/x",
        "rtsp://a@b@[fe80::1]/x",
        "rtsp://u:p@[fe80::1/64%25eth0]/x",
        "rtsp://[fe80::1/64]/x",
        "  rtsp://[fe80::1]/x  ",
        "\trtsp://[fe80::1]/x",
        "rtsp://[fe80::1] ",
        "rtsp://@[fe80::1]/x",
        "rtsp://[::1]@  ",
        "rtsp://a@\u0001/x",
        "rtsp://[fe80::1",
        "rtsp://[]/x",
        "rtsp://   ",
        "rtsp://[FE80::1]/x",
        "rtsp://[::ffff:10.0.0.1]/x",
        "rtsp://[fe80::1]:554/x",
        "rtsp://[fe80::1]/y",
        "rtsp://cam/x",
        string.Empty,
    ];

    [Fact]
    public void The_shared_bracket_scan_answers_as_the_oracle_on_every_input()
    {
        List<string> mismatches = [];

        foreach (var url in Inputs())
        {
            var expected = AuditPayloadBracketScanOracle.BracketContentIsAddressOnly(url);
            var actual = UrlAuthority.BracketContentIsAddressOnly(url);

            if (expected != actual)
            {
                mismatches.Add($"expected {expected}, got {actual}: {Escaped(url)}");
            }
        }

        mismatches.ShouldBeEmpty(string.Join(Environment.NewLine, mismatches.Take(20)));
    }

    [Fact]
    public void The_corpus_holds_inputs_the_oracle_accepts_and_inputs_it_refuses()
    {
        var inputs = Inputs();
        var accepted = inputs.Count(AuditPayloadBracketScanOracle.BracketContentIsAddressOnly);
        var refused = inputs.Count - accepted;
        var measured = $"accepted={accepted} refused={refused}";

        accepted.ShouldBeGreaterThanOrEqualTo(TrueFloor, measured);
        refused.ShouldBeGreaterThanOrEqualTo(FalseFloor, measured);
    }

    private static List<string> Inputs() =>
    [
        .. CameraUrlZoneDelimiterFuzzAdversarialTests.Corpora
            .Cast<object[]>()
            .SelectMany(row => CameraUrlZoneDelimiterFuzzAdversarialTests.Generate((string)row[0], (int)row[1])),
        .. ExplicitRows,
    ];

    private static string Escaped(string text) =>
        string.Concat(text.Select(character => character is < ' ' or > '~' ? $"\\u{(int)character:x4}" : character.ToString()));
}
