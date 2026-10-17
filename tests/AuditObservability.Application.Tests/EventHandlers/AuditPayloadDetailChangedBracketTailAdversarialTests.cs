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

// Spec 392 / issue #3012, adversarial (T070). Bracket tails the written tests leave out (whitespace, control,
// zero-width, fullwidth, percent-encoded, a second bracket, a ']' inside the zone, and text holding an '@') must
// never be claimed as "same origin, only the detail changed": DetailChanged is null in both directions. An '@' in
// the tail still reaches null, through the zone check's own fail-closed bracket scan rather than the placeholder
// (the placeholder side is AuditPayloadDetailChangedBracketTailAdversarialFindingTests). Two plain bracketed
// addresses with a port keep their answer, so the null is not a blanket one.
public class AuditPayloadDetailChangedBracketTailAdversarialTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("rtsp://[fe80::1]\t/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1]\u00A0/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1]\u200B/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1]\uFF3D/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1]%40cam/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1]%5D/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1][fe80::2]/s", "rtsp://[fe80::1]/s")]
    [InlineData("rtsp://[fe80::1%25eth]0]/x", "rtsp://[fe80::1%25eth]1]/x")]
    [InlineData("rtsp://@[fe80::1]x/s", "rtsp://@[fe80::1]y/s")]
    [InlineData("rtsp://user:pass@[fd00::5]x:8554/s", "rtsp://user:pass@[fd00::5]:8554/s")]
    [InlineData("rtsp://@[fe80::1]admin:4242@cam/s", "rtsp://@[fe80::1]admin:9999@cam/s")]
    [InlineData("rtsp://[fe80::1]x@", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://@[fe80::1]garbage.example@x/s", "rtsp://[fe80::1]/s")]
    [InlineData("rtsp://user:pass@[fd00::5]x@cam/s", "rtsp://user:pass@[fd00::5]y@cam/s")]
    public async Task An_odd_bracket_tail_is_never_claimed_as_the_same_origin_in_either_direction(
        string first,
        string second)
    {
        var forward = JsonDocument.Parse(await StoredPayload(first, second)).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload(second, first)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1]:554/x", "rtsp://[fe80::1]:554/y", JsonValueKind.True)]
    [InlineData("rtsp://[::1]:8554/a", "rtsp://[::1]:8554/a?q", JsonValueKind.True)]
    [InlineData("rtsp://[FE80::ABCD]:8554/s", "rtsp://[fe80::abcd]:8554/s", JsonValueKind.True)]
    [InlineData("rtsp://[fe80::1]:554/x", "rtsp://[fe80::2]:554/x", JsonValueKind.False)]
    public async Task Two_plain_bracketed_addresses_keep_their_answer(string first, string second, JsonValueKind expected)
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
