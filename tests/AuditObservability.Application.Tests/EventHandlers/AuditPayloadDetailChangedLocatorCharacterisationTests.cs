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

/// <summary>
/// Characterisation (spec 392 / issue #3017, ADR-0144), captured green on <c>de9802b1</c> before
/// <c>AuditPayloadRedaction.BracketContentIsAddressOnly</c> stops writing its own authority locator and asks
/// <c>UrlAuthority</c>. Every row's answer depends on where the locator finds the authority, in both directions
/// (<c>true</c> where the bracket content is an address, <c>null</c> where it is not). None of these rows may be
/// edited by the refactor; an assertion that has to change means the locator moved.
///
/// <para>
/// Counterfactual, run once on the unchanged code and reverted: removing the <c>Trim()</c> from
/// <c>BracketContentIsAddressOnly</c> fails exactly the first two rows (leading space and leading tab), each
/// going from <c>True</c> to <c>Null</c> in both directions, and no other, so the trim is pinned and the refactor must keep it at the audit call site.
/// </para>
/// </summary>
public class AuditPayloadDetailChangedLocatorCharacterisationTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(" rtsp://[fe80::1]/x ", " rtsp://[fe80::1]/y ", JsonValueKind.True)]
    [InlineData("\trtsp://[fe80::1]/x", "\trtsp://[fe80::1]/y", JsonValueKind.True)]
    [InlineData("\\\\[fe80::1]\\x", "\\\\[fe80::1]\\y", JsonValueKind.True)]
    [InlineData("file:////[fe80::1]/x", "file:////[fe80::1]/y", JsonValueKind.True)]
    [InlineData("file://[fe80::1]/x", "file://[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp:\\\\[fe80::1]/x", "rtsp:\\\\[fe80::1]/y", JsonValueKind.True)]
    [InlineData("RTSP://[FE80::1]/x", "rtsp://[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp:[fe80::1]/x", "rtsp:[fe80::1]/y", JsonValueKind.Null)]
    [InlineData("rtsp://[fe80::1/64]/x", "rtsp://[fe80::1/64]/y", JsonValueKind.Null)]
    [InlineData("rtsp://user@[fe80::1]/x", "rtsp://user@[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp://[fe80::1]:554/x", "rtsp://[fe80::1]:554/y", JsonValueKind.True)]
    [InlineData("rtsp://[fe80::1]:/x", "rtsp://[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp://[fe80::1]?a", "rtsp://[fe80::1]#b", JsonValueKind.True)]
    public async Task The_detail_change_answer_that_depends_on_where_the_authority_starts_is_kept(
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
