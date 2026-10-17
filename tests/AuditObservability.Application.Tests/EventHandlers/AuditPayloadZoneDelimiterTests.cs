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

// Spec 394 / issue #3023 items 1 and 5, red. An '@', '?' or '#' written inside the brackets of an IPv6 address
// makes the origin ambiguous (another parser reads another host), so the side holding it is stored as the
// placeholder, on a registration and on an address change in both directions, and the stored payload carries
// nothing of the hidden host. DetailChanged stays null (unknown), as before.
public class AuditPayloadZoneDelimiterTests
{
    private const string Ordinary = "rtsp://[fe80::1]/x";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-17T08:00:00Z", CultureInfo.InvariantCulture);

    public static TheoryData<string, string> DelimiterInsideTheBrackets => new()
    {
        { "rtsp://@[fe80::1%@10.0.0.66:8554]/live", "10.0.0.66" },
        { "rtsp://@[::1%25@evil.com:554]/s", "evil.com" },
        { "rtsp://@[fe80::1%25eth0@]/s", "eth0@" },
        { "rtsp://[::1%25x?y]/s", "x?y" },
        { "rtsp://[::1%25x#y]/s", "x#y" },
        { "rtsp://[fe80::1%25a?b]/x", "a?b" },
        { "rtsp://[fe80::1%x?y]/s", "x?y" },
        { "rtsp://[::1%25x?y]", "x?y" },
        { "rtsp://[fe80::1%25eth0#]:554/s", "eth0#" },
        { "rtsp://@[::1%25x?y]/s", "x?y" },
    };

    [Theory]
    [MemberData(nameof(DelimiterInsideTheBrackets))]
    public async Task A_registration_over_a_delimiter_inside_the_brackets_stores_the_placeholder(string address, string secret)
    {
        InMemoryAuditEventRepository repo = new();

        await Build(repo).Handle(
            new CameraRegisteredV1(Guid.CreateVersion7(), "n", address, At, Guid.CreateVersion7(), Metadata()),
            default);

        var stored = repo.Committed.ShouldHaveSingleItem().Payload.Content.Value;
        JsonDocument.Parse(stored).RootElement.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        stored.ShouldNotContain(secret, Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(DelimiterInsideTheBrackets))]
    public async Task An_address_change_away_from_a_delimiter_inside_the_brackets_stores_the_placeholder_as_the_previous_url(
        string address,
        string secret)
    {
        var stored = await ChangePayload(address, Ordinary);

        var payload = JsonDocument.Parse(stored).RootElement;
        payload.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        payload.GetProperty("Url").GetString().ShouldBe("rtsp://[fe80::1]");
        payload.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        stored.ShouldNotContain(secret, Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(DelimiterInsideTheBrackets))]
    public async Task An_address_change_to_a_delimiter_inside_the_brackets_stores_the_placeholder_as_the_url(
        string address,
        string secret)
    {
        var stored = await ChangePayload(Ordinary, address);

        var payload = JsonDocument.Parse(stored).RootElement;
        payload.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://[fe80::1]");
        payload.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        payload.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        stored.ShouldNotContain(secret, Case.Sensitive);
    }

    private static async Task<string> ChangePayload(string previous, string url)
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(
            new CameraAddressChangedV1(Guid.CreateVersion7(), "munich", previous, url, At, Guid.CreateVersion7(), Metadata()),
            default);

        return repo.Committed.ShouldHaveSingleItem().Payload.Content.Value;
    }

    private static EventMetadata Metadata() => new(Guid.CreateVersion7(), At, Fab: "munich", Actor: null);

    private static IntegrationEventAuditHandler Build(InMemoryAuditEventRepository repo) =>
        new(new AuditingMessageHandler(
            repo,
            V1ResourceMap.Default,
            new FakeClock(At),
            Options.Create(new AuditMeasurementOptions()),
            NullLogger<AuditingMessageHandler>.Instance));
}
