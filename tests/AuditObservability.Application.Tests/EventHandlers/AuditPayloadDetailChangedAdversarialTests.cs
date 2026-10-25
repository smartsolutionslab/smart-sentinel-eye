using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Spec 384 / issue #2998, adversarial. Every address in the corpus carries the origin a reader would expect
// it to redact to, written by hand, so the expected DetailChanged is modelled independently of
// UrlRedaction and every pair is pushed through the real handler in both orders.
public class AuditPayloadDetailChangedAdversarialTests
{
    private const string MarkerTail = "CRET";
    private const string Placeholder = UrlRedaction.Unparseable;
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly (string? Url, string? Origin)[] Corpus =
    [
        ("rtsp://cam-line4/s", "rtsp://cam-line4"),
        ("rtsp://cam-line4/t?token=SECRET", "rtsp://cam-line4"),
        ("RTSP://CAM-LINE4/s", "rtsp://cam-line4"),
        ("rtsp://user:SECRET@cam-line4/s", "rtsp://cam-line4"),
        ("  rtsp://cam-line4/s  ", "rtsp://cam-line4"),
        ("rtsp://cam-line4/" + new string('x', 70000), "rtsp://cam-line4"),
        ("rtsp://attacker-rtsp/s", "rtsp://attacker-rtsp"),
        ("rtsp://10.0.5.12/h264", "rtsp://10.0.5.12"),
        ("rtsp://10.0.5.12/h265", "rtsp://10.0.5.12"),
        ("rtsp://10.0.5.44/h264", "rtsp://10.0.5.44"),
        ("rtsp://[fd00::5]/x", "rtsp://[fd00::5]"),
        ("rtsp://[fd00:0:0:0:0:0:0:5]/x", "rtsp://[fd00::5]"),
        ("rtsp://cam-line4:8554/s", "rtsp://cam-line4:8554"),
        ("http://cam-line4:80/s", "http://cam-line4"),
        ("http://cam-line4/s", "http://cam-line4"),
        (@"\\cam-line4\s", "file://cam-line4"),
        (@"file:\\cam-line4\s", "file://cam-line4"),
        ("file:////cam-line4/s", "file://cam-line4"),
        ("rtsp://xn--kmera-gra.example/s", "rtsp://xn--kmera-gra.example"),
        ("rtsp://cam-line4/s?x=@", Placeholder),
        ("rtsp://attacker-rtsp/s?x=@", Placeholder),
        ("rtsp://operator:2468#@cam-line4/s", Placeholder),
        (@"rtsp:\\admin:1#@cam-line4", Placeholder),
        ("rtsp://cam-line4/x@SECRET", Placeholder),
        ("rtsp://k\u00E4mera.example/s", Placeholder),
        ("rtsp://cam-line4\uFF0Fs", Placeholder),
        ("rtsp://\uFF43\uFF41\uFF4D-line4/s", Placeholder),
        ("rtsp://\u0430ttacker-rtsp/s", Placeholder),
        ("rtsp://\u212Aam-line4/s", Placeholder),
        ("rtsp://cam\u200E-line4/s", Placeholder),
        ("file:////cam\u200E/x", Placeholder),
        ("file://admin:12345\uDC00#@cam", Placeholder),
        ("rtsp://cam-\tline4/s", Placeholder),
        ("rtsp://cam%2Dline4/s", Placeholder),
        ("rtsp://" + new string('a', 300) + "/s", Placeholder),
        ("rtsp://" + new string('a', 70000), Placeholder),
        ("rtsp:///s", Placeholder),
        ("/s/SECRET", Placeholder),
        (string.Empty, Placeholder),
        (" ", Placeholder),
        ("garbage SECRET1", Placeholder),
        ("garbage SECRET2", Placeholder),
        (Placeholder, Placeholder),
        ("<UNPARSEABLE URL>", Placeholder),
        (" " + Placeholder + " ", Placeholder),
        ("rtsp://" + Placeholder + "/s", Placeholder),
        (null, null),
    ];

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
    public async Task Every_pair_reports_the_detail_change_the_hand_written_origins_imply(int previousIndex)
    {
        var (previous, previousOrigin) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, origin) in Corpus)
        {
            var stored = await StoredPayload(previous, url);
            var root = JsonDocument.Parse(stored).RootElement;
            var expected = ExpectedDetailChanged(previous, previousOrigin, url, origin);
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
    public async Task A_stored_true_always_names_two_read_and_equal_origins(int previousIndex)
    {
        var (previous, _) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, _) in Corpus)
        {
            var root = JsonDocument.Parse(await StoredPayload(previous, url)).RootElement;

            if (root.GetProperty("DetailChanged").ValueKind != JsonValueKind.True)
            {
                continue;
            }

            var storedPrevious = root.GetProperty("PreviousUrl").GetString();
            var storedUrl = root.GetProperty("Url").GetString();

            if (storedPrevious is null
                || string.Equals(storedPrevious, Placeholder, StringComparison.Ordinal)
                || !string.Equals(storedPrevious, storedUrl, StringComparison.Ordinal)
                || string.Equals(previous, url, StringComparison.Ordinal))
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}: true over {storedPrevious} / {storedUrl}");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PreviousAddresses))]
    public async Task Every_pair_stores_one_detail_change_member_in_valid_json_without_a_marker(int previousIndex)
    {
        var (previous, _) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, _) in Corpus)
        {
            var stored = await StoredPayload(previous, url);
            var members = JsonDocument.Parse(stored).RootElement.EnumerateObject()
                .Count(member => string.Equals(member.Name, "DetailChanged", StringComparison.Ordinal));

            if (members != 1 || stored.Contains(MarkerTail, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}: {members} members, marker {stored.Contains(MarkerTail, StringComparison.OrdinalIgnoreCase)}");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PreviousAddresses))]
    public async Task Redelivering_the_audit_copy_never_claims_a_detail_change_and_keeps_an_unknown_unknown(int previousIndex)
    {
        var (previous, _) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, _) in Corpus)
        {
            var root = JsonDocument.Parse(await StoredPayload(previous, url)).RootElement;
            var first = root.GetProperty("DetailChanged").ValueKind;
            var copy = Change(root.GetProperty("PreviousUrl").GetString(), root.GetProperty("Url").GetString());
            var again = JsonDocument.Parse(AuditPayloadRedaction.Serialize(copy)).RootElement.GetProperty("DetailChanged").ValueKind;

            if (again == JsonValueKind.True || (first == JsonValueKind.Null && again != JsonValueKind.Null))
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}: {first} then {again}");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PreviousAddresses))]
    public async Task The_readers_pass_the_detail_change_through_unchanged(int previousIndex)
    {
        var (previous, _) = Corpus[previousIndex];
        List<string> violations = [];

        foreach (var (url, _) in Corpus)
        {
            InMemoryAuditEventRepository repo = new();
            await Build(repo).Handle(Change(previous, url), default);
            var stored = repo.Committed[0].Payload.Content.Value;
            var kind = JsonDocument.Parse(stored).RootElement.GetProperty("DetailChanged").ValueKind;

            var mapped = repo.Committed[0].ToAuditRowDto().Payload;
            var reserialised = JsonSerializer.Serialize(JsonNode.Parse(stored));
            var archivedLine = JsonSerializer.Serialize(new { Payload = stored });
            var archived = JsonDocument.Parse(archivedLine).RootElement.GetProperty("Payload").GetString();

            if (!string.Equals(mapped, stored, StringComparison.Ordinal)
                || JsonDocument.Parse(reserialised).RootElement.GetProperty("DetailChanged").ValueKind != kind
                || !string.Equals(archived, stored, StringComparison.Ordinal))
            {
                violations.Add($"{Shown(previous)} -> {Shown(url)}");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void No_origin_read_from_the_corpus_collides_with_the_placeholder_text()
    {
        foreach (var (url, origin) in Corpus)
        {
            if (url is null || string.Equals(origin, Placeholder, StringComparison.Ordinal))
            {
                continue;
            }

            var read = UrlRedaction.ToOrigin(url);
            read.ShouldNotContain("unparseable", Case.Insensitive, Shown(url));
            read.ShouldContain("://", Case.Sensitive, Shown(url));
        }
    }

    [Fact]
    public async Task The_placeholder_text_written_as_an_address_is_unknown_against_a_read_origin_in_both_orders()
    {
        var forward = JsonDocument.Parse(await StoredPayload(Placeholder, "rtsp://cam-line4/s")).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload("rtsp://cam-line4/s", Placeholder)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        forward.GetProperty("Url").GetString().ShouldBe("rtsp://cam-line4");
        backward.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://cam-line4");
    }

    [Fact]
    public void Appending_the_unknown_member_keeps_the_json_valid_with_one_null_member()
    {
        var appended = AuditPayloadRedaction.AppendMember(typeof(CameraAddressChangedV1), "{\"A\":1}", "\"DetailChanged\":null");

        var root = JsonDocument.Parse(appended).RootElement;
        root.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        root.EnumerateObject().Count().ShouldBe(2);
    }

    [Theory]
    [InlineData("{\"A\":1}\n")]
    [InlineData("null")]
    public void Appending_the_unknown_member_to_a_serialisation_without_a_closing_brace_is_refused(string json)
    {
        Should.Throw<InvalidOperationException>(
            () => AuditPayloadRedaction.AppendMember(typeof(CameraAddressChangedV1), json, "\"DetailChanged\":null"));
    }

    private static JsonValueKind ExpectedDetailChanged(string? previous, string? previousOrigin, string? url, string? origin)
    {
        if (previous is null || url is null)
        {
            return JsonValueKind.False;
        }

        if (string.Equals(previousOrigin, Placeholder, StringComparison.Ordinal)
            || string.Equals(origin, Placeholder, StringComparison.Ordinal))
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
