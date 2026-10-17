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

// Spec 392 / issue #3012, adversarial finding (T070), red against the fix. When the text after the closing ']'
// holds an '@', or a '/' inside the brackets makes the parser read an address nobody wrote, the audit stores an
// origin naming a host the address did not: UrlAuthority.TextFollowsTheClosingBracket ends the userinfo at the last
// '@' before the first terminator where the parser ends it at the first, so the side is not withheld. The side is
// to be stored as the placeholder, as every other bracket tail is.
public class AuditPayloadDetailChangedBracketTailAdversarialFindingTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("rtsp://@[fe80::1]admin:4242@cam/s", "rtsp://[fe80::1]/s", UrlRedaction.Unparseable, "rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1]x@", "rtsp://[fe80::1]/x", UrlRedaction.Unparseable, "rtsp://[fe80::1]")]
    [InlineData("rtsp://user:pass@[fd00::5]x@cam/s", "rtsp://[fd00::5]/s", UrlRedaction.Unparseable, "rtsp://[fd00::5]")]
    [InlineData("rtsp://[fe80/::1]/s", "rtsp://[fe80::]/s", UrlRedaction.Unparseable, "rtsp://[fe80::]")]
    public async Task The_side_whose_written_host_the_parser_did_not_read_is_stored_as_the_placeholder(
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
