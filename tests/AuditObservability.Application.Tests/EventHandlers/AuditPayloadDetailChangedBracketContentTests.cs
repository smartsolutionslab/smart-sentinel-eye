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

// Spec 387 / issue #3006, blocker B1. .NET's Uri parser drops a zone written after a prefix length inside the
// brackets, so the parsed host carries no '%'. The rule: an IPv6 host whose WRITTEN bracket text holds anything
// besides hex digits, ':' and '.' is unknown (null), never "same origin". Text after ']' is a different class.
public class AuditPayloadDetailChangedBracketContentTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);
    private static readonly string[] Markers = ["eth", "SECRET"];

    [Theory]
    [InlineData("rtsp://[fe80::1/64%25eth0]/s", "rtsp://[fe80::1/64%25eth1]/s")]
    [InlineData("rtsp://[fe80::1/64%eth0]/s", "rtsp://[fe80::1/64%eth1]/s")]
    [InlineData("rtsp://[fe80::1/64%25SECRET]/s", "rtsp://[fe80::1]/t")]
    [InlineData("rtsp://[fe80::1/64]/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1/64:1]/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1/128%25eth0]/x", "rtsp://[fe80::1/128%25eth1]/x")]
    [InlineData("rtsp://[fe80::1/64%25eth0]/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1/0]/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[FE80::0001/64]/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[::ffff:10.0.0.1/96%25eth0]/x", "rtsp://[::ffff:10.0.0.1/96%25eth1]/x")]
    [InlineData("rtsp://[::ffff:10.0.0.1/96%25eth0]/x", "rtsp://[::ffff:10.0.0.1]/x")]
    public async Task A_prefix_length_or_zone_written_inside_the_brackets_makes_the_detail_change_unknown(
        string first,
        string second)
    {
        var forward = JsonDocument.Parse(await StoredPayload(first, second)).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload(second, first)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1/64%25eth0]/s", "rtsp://[fe80::1/64%25eth1]/s")]
    [InlineData("rtsp://[fe80::1/64%eth0]/s", "rtsp://[fe80::1/64%eth1]/s")]
    [InlineData("rtsp://[fe80::1/64%25SECRET]/s", "rtsp://[fe80::1]/t")]
    [InlineData("rtsp://[fe80::1/128%25eth0]/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[::ffff:10.0.0.1/96%25eth0]/x", "rtsp://[::ffff:10.0.0.1/96%25eth1]/x")]
    public async Task The_zone_text_inside_the_brackets_reaches_neither_the_stored_payload_nor_the_mapped_row(
        string first,
        string second)
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(Change(first, second), default);
        var row = repo.Committed.ShouldHaveSingleItem();
        var stored = row.Payload.Content.Value;
        var mapped = AuditRowMapper.Map(row).Payload;

        foreach (var marker in Markers)
        {
            stored.ShouldNotContain(marker, Case.Insensitive);
            mapped.ShouldNotContain(marker, Case.Insensitive);
        }
    }

    // Characterisation: green today and after. Written-bracket text of hex, ':' and '.' alone keeps the ordinal rule,
    // hostnames and IPv4 are untouched, and the existing zone rows stay unknown.
    [Theory]
    [InlineData("rtsp://[fe80::1]/x", "rtsp://[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp://[FE80::0001]/x", "rtsp://[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp://[::ffff:7f00:1]/x", "rtsp://[::ffff:127.0.0.1]/y", JsonValueKind.True)]
    [InlineData("rtsps://[fe80::1]:8554/x", "rtsps://[fe80::1]:8554/y", JsonValueKind.True)]
    [InlineData("rtsps://[fe80::1]:8554/x", "rtsps://[fe80::1]:8555/x", JsonValueKind.False)]
    [InlineData("rtsp://[fe80::1]/x", "rtsp://[fe80::2]/x", JsonValueKind.False)]
    [InlineData("rtsp://[fe80::1]/x", "rtsp://[fe80::1]/x", JsonValueKind.False)]
    [InlineData("rtsp://cam/x", "rtsp://cam/y", JsonValueKind.True)]
    [InlineData("rtsp://cam/x", "rtsp://other/x", JsonValueKind.False)]
    [InlineData("rtsp://10.0.5.12/x", "rtsp://10.0.5.12/y", JsonValueKind.True)]
    [InlineData("rtsp://10.0.5.12/x", "rtsp://10.0.5.13/x", JsonValueKind.False)]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25eth1]/x", JsonValueKind.Null)]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1]/x", JsonValueKind.Null)]
    public async Task Brackets_holding_only_address_characters_keep_the_existing_detail_change_rule(
        string first,
        string second,
        JsonValueKind expected)
    {
        var forward = JsonDocument.Parse(await StoredPayload(first, second)).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload(second, first)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(expected);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(expected);
    }

    private static async Task<string> StoredPayload(string previous, string url)
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(Change(previous, url), default);

        return repo.Committed.ShouldHaveSingleItem().Payload.Content.Value;
    }

    private static CameraAddressChangedV1 Change(string previous, string url) => new(
        Guid.CreateVersion7(),
        "munich",
        previous,
        url,
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
}
