using System.Text.Json;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 416 / issue #3023 item 6, characterisation. The audit's <c>DetailChanged</c> (true / false / null) must be
/// what it was at <c>d24ee84e</c> for every address, before and after the bracket scan folds onto
/// <c>UrlAuthority</c>. The expected value comes from <see cref="AuditPayloadBracketScanOracle"/>, a frozen copy of
/// that code; the actual value is read from the live serialiser. Every input of the six fuzz corpora plus explicit
/// rows is paired with a fixed partner in both directions, and reach floors keep the comparison from passing vacuously.
/// </summary>
public class AuditPayloadBracketFoldCharacterisationTests
{
    private const string Partner = "rtsp://[fe80::1]/s";

    // Measured on the six seeds plus the explicit rows at d24ee84e (T111): reached=980, userinfoBracket=208,
    // addressOnly=608, refusedWithoutUserinfoBracket=164. Each floor is about 90% of the measurement.
    private const int ReachedFloor = 880;
    private const int UserinfoBracketFloor = 185;
    private const int AddressOnlyFloor = 545;
    private const int RefusedFloor = 145;

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
    ];

    [Fact]
    public void The_audits_zone_answer_equals_the_oracle_on_every_corpus_input_in_both_directions()
    {
        List<string> mismatches = [];

        foreach (var url in Inputs())
        {
            Compare(Partner, url, mismatches);
            Compare(url, Partner, mismatches);
        }

        mismatches.ShouldBeEmpty(string.Join(Environment.NewLine, mismatches.Take(20)));
    }

    [Fact]
    public void The_address_only_row_and_the_zoned_prefix_rows_keep_their_documented_answers()
    {
        Live("rtsp://[fe80::1]/x", "rtsp://[fe80::1]/y").ShouldBe("true");
        Live("rtsp://u:p@[fe80::1/64%25eth0]/x", Partner).ShouldBe("null");
        Live(Partner, "rtsp://u:p@[fe80::1/64%25eth0]/x").ShouldBe("null");
        Live("rtsp://[]@[FE80::1]", Partner).ShouldBe("null");
    }

    [Fact]
    public void The_corpora_reach_the_bracket_scan_and_each_of_its_arms()
    {
        var reached = 0;
        var userinfoBracket = 0;
        var addressOnly = 0;
        var refusedWithoutUserinfoBracket = 0;

        foreach (var url in Inputs().Where(AuditPayloadBracketScanOracle.IsBracketCheckReached))
        {
            reached++;

            if (AuditPayloadBracketScanOracle.HasBracketInTheUserinfo(url))
            {
                userinfoBracket++;
            }
            else if (AuditPayloadBracketScanOracle.BracketContentIsAddressOnly(url))
            {
                addressOnly++;
            }
            else
            {
                refusedWithoutUserinfoBracket++;
            }
        }

        var measured = $"reached={reached} userinfoBracket={userinfoBracket} addressOnly={addressOnly} "
            + $"refusedWithoutUserinfoBracket={refusedWithoutUserinfoBracket}";

        reached.ShouldBeGreaterThanOrEqualTo(ReachedFloor, measured);
        userinfoBracket.ShouldBeGreaterThanOrEqualTo(UserinfoBracketFloor, measured);
        addressOnly.ShouldBeGreaterThanOrEqualTo(AddressOnlyFloor, measured);
        refusedWithoutUserinfoBracket.ShouldBeGreaterThanOrEqualTo(RefusedFloor, measured);
    }

    private static List<string> Inputs() =>
    [
        .. CameraUrlZoneDelimiterFuzzAdversarialTests.Corpora
            .Cast<object[]>()
            .SelectMany(row => CameraUrlZoneDelimiterFuzzAdversarialTests.Generate((string)row[0], (int)row[1])),
        .. ExplicitRows,
    ];

    private static void Compare(string previous, string url, List<string> mismatches)
    {
        var expected = Render(AuditPayloadBracketScanOracle.DetailChanged(previous, url));
        var actual = Live(previous, url);

        if (expected != actual)
        {
            mismatches.Add($"expected {expected}, got {actual}: previous={Escaped(previous)} url={Escaped(url)}");
        }
    }

    private static string Live(string previous, string url)
    {
        var json = AuditPayloadRedaction.Serialize(Change(previous, url));
        using var document = JsonDocument.Parse(json);

        return Render(document.RootElement.GetProperty("DetailChanged").ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => (bool?)null,
        });
    }

    private static string Render(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "null",
    };

    private static string Escaped(string text) =>
        string.Concat(text.Select(character => character is < ' ' or > '~' ? $"\\u{(int)character:x4}" : character.ToString()));

    private static CameraAddressChangedV1 Change(string previous, string url) =>
        new(
            Guid.Empty,
            "dresden",
            previous,
            url,
            DateTimeOffset.UnixEpoch,
            Guid.Empty,
            new EventMetadata(Guid.Empty, DateTimeOffset.UnixEpoch, "dresden", null));
}
