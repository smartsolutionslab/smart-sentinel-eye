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

// Spec 392 / issue #3012, red. Text written after the closing ']' of an IPv6 host is dropped by the parser, so
// two addresses that differ only there render the same origin and the audit used to claim "same origin, only the
// detail changed". The side holding such text is stored as the placeholder and the claim is unknown (null), in
// both directions: never true, never false.
public class AuditPayloadDetailChangedBracketTailTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("rtsp://[fe80::1]%25eth0/x", "rtsp://[fe80::1]%25eth1/x")]
    [InlineData("rtsp://[fe80::1]%eth0/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1]garbage.example/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1]x:554/s", "rtsp://[fe80::1]/s")]
    [InlineData("rtsp://[fe80::1]]/x", "rtsp://[fe80::1]/x")]
    public async Task Text_written_after_the_closing_bracket_is_not_claimed_as_the_same_origin(
        string first,
        string second)
    {
        var forward = JsonDocument.Parse(await StoredPayload(first, second)).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload(second, first)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1]%25eth0/x", "rtsp://[fe80::1]%25eth1/x", UrlRedaction.Unparseable, UrlRedaction.Unparseable)]
    [InlineData("rtsp://[fe80::1]%eth0/x", "rtsp://[fe80::1]/x", UrlRedaction.Unparseable, "rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1]garbage.example/x", "rtsp://[fe80::1]/x", UrlRedaction.Unparseable, "rtsp://[fe80::1]")]
    public async Task The_side_holding_text_after_the_bracket_is_stored_as_the_placeholder(
        string previous,
        string url,
        string expectedPrevious,
        string expectedUrl)
    {
        var payload = JsonDocument.Parse(await StoredPayload(previous, url)).RootElement;

        payload.GetProperty("PreviousUrl").GetString().ShouldBe(expectedPrevious);
        payload.GetProperty("Url").GetString().ShouldBe(expectedUrl);
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
