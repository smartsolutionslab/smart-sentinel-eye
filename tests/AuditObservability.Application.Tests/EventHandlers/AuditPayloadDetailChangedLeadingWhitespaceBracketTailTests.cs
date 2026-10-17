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

// Spec 392 / issue #3012, red. The parser skips leading whitespace and control characters, so text after the closing
// ']' behind such a prefix was dropped from the host and the audit claimed "same origin, only the detail changed".
// The claim is unknown (null) and the side holding the text is stored as the placeholder, as without the prefix.
public class AuditPayloadDetailChangedLeadingWhitespaceBracketTailTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(" rtsp://[fe80::1]%25eth0/x", " rtsp://[fe80::1]%25eth1/x")]
    [InlineData("\nrtsp://[fe80::1]x/s", "\nrtsp://[fe80::1]y/s")]
    [InlineData("\trtsp://[fe80::1]garbage/x", "\trtsp://[fe80::1]other/x")]
    [InlineData(" rtsp://[fe80::1]%eth0/x", " rtsp://[fe80::1]/x")]
    [InlineData("\rrtsp://[fe80::1]garbage.example/x", "rtsp://[fe80::1]/x")]
    public async Task Text_after_the_bracket_behind_a_leading_whitespace_prefix_is_not_claimed_as_the_same_origin(
        string first,
        string second)
    {
        var forward = JsonDocument.Parse(await StoredPayload(first, second)).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload(second, first)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_side_holding_text_after_the_bracket_behind_a_prefix_is_stored_as_the_placeholder()
    {
        var payload = JsonDocument.Parse(await StoredPayload(" rtsp://[fe80::1]%25eth0/x", " rtsp://[fe80::1]%25eth1/x")).RootElement;

        payload.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        payload.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [InlineData(" rtsp://[fe80::1]/x", " rtsp://[fe80::1]/y")]
    [InlineData("\nrtsp://cam/x", "\nrtsp://cam/y")]
    public async Task A_clean_host_behind_a_leading_whitespace_prefix_still_claims_the_same_origin(
        string first,
        string second)
    {
        var payload = JsonDocument.Parse(await StoredPayload(first, second)).RootElement;

        payload.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.True);
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
