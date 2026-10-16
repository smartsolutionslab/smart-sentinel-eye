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

// Spec 387 / issue #3006, blocker B2. A '[' in the USERINFO must not be mistaken for the host's bracket: the rule
// reads the bracket content of the HOST, and fails closed (unknown) when the userinfo itself holds '[' or ']'.
public class AuditPayloadDetailChangedUserinfoBracketTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("rtsp://[]@[fe80::1/64%25eth0]/s", "rtsp://[]@[fe80::1/64%25eth1]/t")]
    [InlineData("rtsp://[1:2]@[fe80::1/64%25eth0]/s", "rtsp://[1:2]@[fe80::1/64%25eth1]/t")]
    [InlineData("rtsp://[a]:p@[fe80::1/64%25eth0]/s", "rtsp://[a]:p@[fe80::1/64%25eth1]/s")]
    [InlineData("rtsp://[ad]:pw@[fe80::1/64%25eth0]/s", "rtsp://[ad]:pw@[fe80::1/64%25eth1]/s")]
    [InlineData("rtsp://[]:x@[fe80::1/64%25eth0]/s", "rtsp://[]:y@[fe80::1/64%25eth1]/s")]
    [InlineData("rtsp://[]@[fe80::1/64%25eth0]:554/s", "rtsp://[]@[fe80::1/64%25eth1]:554/s")]
    [InlineData("rtsp:\\[]@[fe80::1/64%25eth0]/x", "rtsp:\\[]@[fe80::1/64%25eth1]/x")]
    public async Task A_bracket_in_the_userinfo_does_not_hide_a_zone_behind_a_prefix_length(
        string first,
        string second)
    {
        var forward = JsonDocument.Parse(await StoredPayload(first, second)).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload(second, first)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // False-unknown accepted: userinfo with a bracket. Fail-closed trades a missed "changed" signal for never
    // reporting a hidden zone as the same origin.
    [Fact]
    public async Task False_unknown_accepted_userinfo_with_a_bracket_makes_the_detail_change_unknown()
    {
        var forward = JsonDocument.Parse(await StoredPayload("rtsp://us[er@[fe80::1]/x", "rtsp://us[er@[fe80::1]/y")).RootElement;
        var backward = JsonDocument.Parse(await StoredPayload("rtsp://us[er@[fe80::1]/y", "rtsp://us[er@[fe80::1]/x")).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // Characterisation: green today and after.
    [Theory]
    [InlineData("rtsp://user:p@[fe80::1]/x", "rtsp://user:p@[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp://us%5Ber@[fe80::1]/x", "rtsp://us%5Ber@[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp://user@[fe80::1]/x", "rtsp://user@[fe80::1]/y", JsonValueKind.True)]
    [InlineData("rtsp://user@[fe80::1]/x", "rtsp://user@[fe80::2]/x", JsonValueKind.False)]
    [InlineData("rtsp://user:p@cam/x", "rtsp://user:p@cam/y", JsonValueKind.True)]
    [InlineData("rtsp://user@[fe80::1%25eth0]/x", "rtsp://user@[fe80::1%25eth1]/x", JsonValueKind.Null)]
    public async Task A_userinfo_without_brackets_keeps_the_existing_detail_change_rule(
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
