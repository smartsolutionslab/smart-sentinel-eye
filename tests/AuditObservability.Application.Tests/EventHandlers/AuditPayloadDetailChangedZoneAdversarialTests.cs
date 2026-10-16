using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Spec 387 / issue #3006, adversarial. Every address carries the origin it should redact to and whether its host
// carries an IPv6 zone, both written by hand rather than computed by the code under test, and every pair is pushed
// through the real handler in both orders. The percent-outside-the-host entries are zone-free: a textual search
// for '%' anywhere in the address would wrongly call them unknown.
public class AuditPayloadDetailChangedZoneAdversarialTests
{
    private const string Placeholder = UrlRedaction.Unparseable;
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);
    private static readonly string LongZone = new('z', 300);
    private static readonly string[] Markers = ["eth", "CRET", "zzzz", "%"];

    private static readonly (string? Url, string? Origin, bool Zoned)[] Corpus =
    [
        ("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25eth1]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25eth0]/y", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%eth0]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%1]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%251]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25ETH0]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25Eth0]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%2525eth0]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%2565th0]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25e%74h0]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%09]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25%20]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25eth0%25eth1]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25" + LongZone + "]/x", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80:0:0:0:0:0:0:1%25eth0]/x", "rtsp://[fe80::1]", true),
        ("RTSP://[FE80::1%25eth0]/x", "rtsp://[fe80::1]", true),
        ("rtsp://user:SECRET@[fe80::1%25eth0]/x", "rtsp://[fe80::1]", true),
        ("  rtsp://[fe80::1%25eth0]/x  ", "rtsp://[fe80::1]", true),
        ("rtsp://[fe80::1%25eth0]/a%20b", "rtsp://[fe80::1]", true),
        ("rtsp://[::ffff:10.0.0.1%25eth0]/x", "rtsp://[::ffff:10.0.0.1]", true),
        ("rtsp://[::1%25lo]/x", "rtsp://[::1]", true),
        ("rtsp://[::%25eth0]/x", "rtsp://[::]", true),
        ("rtsp://[fd00::5%25eth0]/x", "rtsp://[fd00::5]", true),
        ("rtsps://[fe80::1%25eth0]:8554/x", "rtsps://[fe80::1]:8554", true),
        ("http://[fe80::1%25eth0]/x", "http://[fe80::1]", true),
        ("http://[fe80::1%25eth0]:80/x", "http://[fe80::1]", true),
        ("http://[fe80::1%25eth0]:8080/x", "http://[fe80::1]:8080", true),
        ("file://[fe80::1%25eth0]/s", "file://[fe80::1]", true),
        (@"\\[fe80::1%25eth0]\s", "file://[fe80::1]", true),
        (@"file:\\[fe80::1%25eth0]\s", "file://[fe80::1]", true),

        ("rtsp://[fe80::1]/x", "rtsp://[fe80::1]", false),
        ("rtsp://[fe80::1]/y", "rtsp://[fe80::1]", false),
        ("rtsp://[fe80::2]/x", "rtsp://[fe80::2]", false),
        ("rtsp://[fe80:0:0:0:0:0:0:1]/x", "rtsp://[fe80::1]", false),
        ("rtsp://[fd00::5]/x", "rtsp://[fd00::5]", false),
        ("rtsp://[fd00:0:0:0:0:0:0:5]/x", "rtsp://[fd00::5]", false),
        ("rtsp://[::ffff:10.0.0.1]/x", "rtsp://[::ffff:10.0.0.1]", false),
        ("rtsp://[::1]/x", "rtsp://[::1]", false),
        ("rtsps://[fe80::1]:8554/x", "rtsps://[fe80::1]:8554", false),
        ("http://[fe80::1]/x", "http://[fe80::1]", false),
        ("file://[fe80::1]/s", "file://[fe80::1]", false),
        ("rtsp://cam/x", "rtsp://cam", false),
        ("rtsp://10.0.5.12/x", "rtsp://10.0.5.12", false),

        ("rtsp://[fe80::1]/a%20b", "rtsp://[fe80::1]", false),
        ("rtsp://[fe80::1]/c%20d", "rtsp://[fe80::1]", false),
        ("rtsp://[fe80::1]/a%2520b", "rtsp://[fe80::1]", false),
        ("rtsp://[fe80::1]/%25eth0", "rtsp://[fe80::1]", false),
        ("rtsp://[fe80::1]/?z=%25eth1", "rtsp://[fe80::1]", false),
        ("rtsp://[fe80::1]/x#%25eth0", "rtsp://[fe80::1]", false),
        ("rtsp://[fe80::2]/%25eth0", "rtsp://[fe80::2]", false),
        ("rtsps://[fe80::1]:8554/%41", "rtsps://[fe80::1]:8554", false),
        ("rtsp://[::ffff:10.0.0.1]/a%20b", "rtsp://[::ffff:10.0.0.1]", false),
        ("rtsp://cam/a%20b", "rtsp://cam", false),
        ("rtsp://cam/?q=%25", "rtsp://cam", false),
        ("rtsp://10.0.5.12/a%2Fb", "rtsp://10.0.5.12", false),

        ("rtsp://fe80::1%25eth0/x", Placeholder, false),
        ("rtsp://[fe80::1%25eth0/x", Placeholder, false),
        ("rtsp://10.0.0.1%25eth0/x", Placeholder, false),
        ("rtsp://cam%25eth0/x", Placeholder, false),
        ("rtsp://[fe80::1%25\u00E9th0]/x", Placeholder, false),
        ("rtsp://[fe80::1%25eth0]/s?x=@", Placeholder, false),
        ("rtsp://cam/s?x=@", Placeholder, false),

        (null, null, false),
    ];

    // The gap counterfactual (d) left open: a '%' outside the host of a zone-free address keeps the ordinal rule.
    [Theory]
    [InlineData("rtsp://[fe80::1]/a%20b", "rtsp://[fe80::1]/c%20d", true)]
    [InlineData("rtsp://[fe80::1]/a%20b", "rtsp://[fe80::1]/a%2520b", true)]
    [InlineData("rtsp://[fe80::1]/a%20b", "rtsp://[fe80::1]/a%20b", false)]
    [InlineData("rtsp://[fe80::1]/x", "rtsp://[fe80::1]/x?z=%25eth1", true)]
    [InlineData("rtsp://[fe80::1]/x#%25eth0", "rtsp://[fe80::1]/x", true)]
    [InlineData("rtsp://[fe80::1]/%25eth0", "rtsp://[fe80::2]/%25eth0", false)]
    [InlineData("rtsps://[fe80::1]:8554/%41", "rtsps://[fe80::1]:8554/A", true)]
    [InlineData("rtsp://[::ffff:10.0.0.1]/a%20b", "rtsp://[::ffff:10.0.0.1]/c", true)]
    [InlineData("rtsp://cam/a%20b", "rtsp://cam/c%20d", true)]
    [InlineData("rtsp://cam/?q=%25", "rtsp://other/?q=%25", false)]
    [InlineData("rtsp://10.0.5.12/a%2Fb", "rtsp://10.0.5.12/a/b", true)]
    public async Task A_percent_outside_the_host_of_a_zone_free_address_keeps_the_ordinal_rule(
        string first,
        string second,
        bool expected)
    {
        var forward = JsonDocument.Parse(await StoredPayload(first, second)).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload(second, first)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(expected ? JsonValueKind.True : JsonValueKind.False);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(expected ? JsonValueKind.True : JsonValueKind.False);
    }

    public static TheoryData<int> PreviousAddresses()
    {
        TheoryData<int> data = [];

        foreach (var index in Enumerable.Range(0, Corpus.Length))
        {
            data.Add(index);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PreviousAddresses))]
    public async Task Every_pair_reports_the_detail_change_the_hand_written_origins_and_zones_imply(int previousIndex)
    {
        var (previous, previousOrigin, previousZoned) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, origin, zoned) in Corpus)
        {
            var root = JsonDocument.Parse(await StoredPayload(previous, url)).RootElement;
            var expected = Expected(previous, previousOrigin, previousZoned, url, origin, zoned);
            var actual = root.GetProperty("DetailChanged").ValueKind;

            if (actual != expected)
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}: expected {expected}, stored {actual}");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PreviousAddresses))]
    public async Task Every_pair_stores_the_hand_written_origins(int previousIndex)
    {
        var (previous, previousOrigin, _) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, origin, _) in Corpus)
        {
            var root = JsonDocument.Parse(await StoredPayload(previous, url)).RootElement;
            var storedPrevious = root.GetProperty("PreviousUrl").GetString();
            var storedUrl = root.GetProperty("Url").GetString();

            if (!string.Equals(storedPrevious, previousOrigin, StringComparison.Ordinal)
                || !string.Equals(storedUrl, origin, StringComparison.Ordinal))
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}: stored {storedPrevious} / {storedUrl}");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PreviousAddresses))]
    public async Task No_pair_with_a_zoned_side_stores_a_known_detail_change(int previousIndex)
    {
        var (previous, _, previousZoned) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, _, zoned) in Corpus)
        {
            if (previous is null || url is null || !(previousZoned || zoned))
            {
                continue;
            }

            var kind = JsonDocument.Parse(await StoredPayload(previous, url)).RootElement.GetProperty("DetailChanged").ValueKind;

            if (kind != JsonValueKind.Null)
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}: stored {kind}");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PreviousAddresses))]
    public async Task Every_pair_stores_and_maps_one_detail_change_member_in_valid_json_without_zone_text(int previousIndex)
    {
        var (previous, _, _) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, _, _) in Corpus)
        {
            InMemoryAuditEventRepository repo = new();
            await Build(repo).Handle(Change(previous, url), default);
            var row = repo.Committed.ShouldHaveSingleItem();
            var stored = row.Payload.Content.Value;
            var mapped = AuditRowMapper.Map(row).Payload;
            var members = JsonDocument.Parse(stored).RootElement.EnumerateObject()
                .Count(member => string.Equals(member.Name, "DetailChanged", StringComparison.Ordinal));
            var leaked = Markers.Where(marker =>
                stored.Contains(marker, StringComparison.OrdinalIgnoreCase)
                || mapped.Contains(marker, StringComparison.OrdinalIgnoreCase));

            if (members != 1 || !string.Equals(mapped, stored, StringComparison.Ordinal) || leaked.Any())
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}: {members} members, leaked [{string.Join(",", leaked)}]");
            }
        }

        violations.ShouldBeEmpty();
    }

    // The stored copy holds only zone-free origins by design (spec 387 §4), so a redelivered copy cannot keep a
    // zoned pair unknown; what it must never do is claim the change stayed on one origin.
    [Theory]
    [MemberData(nameof(PreviousAddresses))]
    public async Task Redelivering_the_audit_copy_of_a_zoned_pair_never_claims_a_detail_change(int previousIndex)
    {
        var (previous, _, _) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, _, _) in Corpus)
        {
            var root = JsonDocument.Parse(await StoredPayload(previous, url)).RootElement;
            var copy = Change(root.GetProperty("PreviousUrl").GetString(), root.GetProperty("Url").GetString());
            var again = JsonDocument.Parse(AuditPayloadRedaction.Serialize(copy)).RootElement.GetProperty("DetailChanged").ValueKind;

            if (again == JsonValueKind.True)
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}: redelivered as true");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_seventy_thousand_character_zone_is_unknown_and_never_stored()
    {
        var zone = new string('z', 70000);

        var root = JsonDocument.Parse(await StoredPayload($"rtsp://[fe80::1%25{zone}]/x", "rtsp://[fe80::1]/x")).RootElement;

        root.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://[fe80::1]");
        root.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static JsonValueKind Expected(
        string? previous,
        string? previousOrigin,
        bool previousZoned,
        string? url,
        string? origin,
        bool zoned)
    {
        if (previous is null || url is null)
        {
            return JsonValueKind.False;
        }

        if (string.Equals(previousOrigin, Placeholder, StringComparison.Ordinal)
            || string.Equals(origin, Placeholder, StringComparison.Ordinal)
            || previousZoned
            || zoned)
        {
            return JsonValueKind.Null;
        }

        return string.Equals(previousOrigin, origin, StringComparison.Ordinal)
            && !string.Equals(previous, url, StringComparison.Ordinal)
                ? JsonValueKind.True
                : JsonValueKind.False;
    }

    private static async Task<string> StoredPayload(string? previous, string? url)
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(Change(previous, url), default);

        return repo.Committed.ShouldHaveSingleItem().Payload.Content.Value;
    }

    private static CameraAddressChangedV1 Change(string? previous, string? url) => new(
        Guid.CreateVersion7(),
        "munich",
        previous!,
        url!,
        At,
        Guid.CreateVersion7(),
        new EventMetadata(Guid.CreateVersion7(), At, Fab: "munich", Actor: null));

    private static IntegrationEventAuditHandler Build(InMemoryAuditEventRepository repo) =>
        new(new AuditingMessageHandler(
            repo,
            V1ResourceMap.Default,
            new FakeClock(At),
            Options.Create(new AuditMeasurementOptions()),
            NullLogger<AuditingMessageHandler>.Instance));

    private static string Shown(string? url)
    {
        if (url is null)
        {
            return "null";
        }

        return url.Length > 60 ? $"{url[..40]}...({url.Length})" : url;
    }
}
