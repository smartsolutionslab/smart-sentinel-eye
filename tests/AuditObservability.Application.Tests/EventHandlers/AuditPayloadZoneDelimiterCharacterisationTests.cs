using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Characterisation (spec 394 / issue #3023, ADR-0144), captured green on fbd4176b before ToOrigin withholds '@',
// '?' and '#' inside the IPv6 brackets. DetailChanged is already null (unknown) in both directions for every such
// address, because the zone arm sees the '%'; and an address whose host or userinfo the parser cannot read is
// audited without throwing. Neither outcome may change.
public class AuditPayloadZoneDelimiterCharacterisationTests
{
    private const string Ordinary = "rtsp://[fe80::1]/x";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-17T08:00:00Z", CultureInfo.InvariantCulture);

    public static TheoryData<string> DelimiterInsideTheBrackets =>
    [
        "rtsp://@[fe80::1%@10.0.0.66:8554]/live",
        "rtsp://@[::1%25@evil.com:554]/s",
        "rtsp://@[fe80::1%25eth0@]/s",
        "rtsp://[::1%25x?y]/s",
        "rtsp://[::1%25x#y]/s",
        "rtsp://[fe80::1%25a?b]/x",
        "rtsp://[fe80::1%x?y]/s",
        "rtsp://[::1%25x?y]",
        "rtsp://[fe80::1%25eth0#]:554/s",
        "rtsp://@[::1%25x?y]/s",
    ];

    public static TheoryData<string> UnreadableHostOrUserInfo =>
    [
        "rtsp://[1:2:3:4:5:6:7:8:/64]",
        "rtsp://[::1:2:3:4:5:6:7:/64]/s",
        "rtsp://[1:2:3:4:5:6:7:8:/64]/live",
        "rtsp://[1:2:3:4:5:6:7:8:/64]x/s",
        "RTSP://@[1:2:3:4:5:6:7:8:/64%25eth0]",
        "RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s",
        "rtsp://[]@[1:2:3:4:5:6:7:8:/64]x/s",
    ];

    [Theory]
    [MemberData(nameof(DelimiterInsideTheBrackets))]
    public async Task A_delimiter_inside_the_brackets_leaves_the_detail_claim_unknown_in_both_directions(string address)
    {
        var forward = JsonDocument.Parse(await ChangePayload(address, Ordinary)).RootElement;
        var backward = JsonDocument.Parse(await ChangePayload(Ordinary, address)).RootElement;

        forward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        backward.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Two_addresses_hiding_a_host_in_the_zone_leave_the_detail_claim_unknown()
    {
        var payload = JsonDocument.Parse(
            await ChangePayload("rtsp://@[fe80::1%@10.0.0.66:8554]/live", "rtsp://@[fe80::1%@10.0.0.66:8554]/x")).RootElement;

        payload.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [MemberData(nameof(UnreadableHostOrUserInfo))]
    public async Task An_address_the_parser_cannot_read_is_audited_on_a_change_without_throwing(string address)
    {
        var forward = await Record.ExceptionAsync(() => ChangePayload(address, Ordinary));
        var backward = await Record.ExceptionAsync(() => ChangePayload(Ordinary, address));

        forward.ShouldBeNull();
        backward.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(UnreadableHostOrUserInfo))]
    public async Task An_address_the_parser_cannot_read_is_audited_on_a_registration_without_throwing(string address)
    {
        InMemoryAuditEventRepository repo = new();

        var thrown = await Record.ExceptionAsync(() => Build(repo).Handle(
            new CameraRegisteredV1(Guid.CreateVersion7(), "n", address, At, Guid.CreateVersion7(), Metadata()),
            default));

        thrown.ShouldBeNull();
        repo.Committed.ShouldHaveSingleItem();
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        DelimiterInsideTheBrackets.Cast<object[]>().Count().ShouldBe(10);
        UnreadableHostOrUserInfo.Cast<object[]>().Count().ShouldBe(7);
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
