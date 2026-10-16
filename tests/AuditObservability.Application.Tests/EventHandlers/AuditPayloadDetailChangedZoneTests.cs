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

// Spec 387 / issue #3006. An IPv6 zone is dropped from the stored origin, so an origin with its zone dropped was
// not fully read: the audit row must not claim the change stayed on one origin. Assertions are on the stored
// payload and the mapped row, with hand-written expectations.
public class AuditPayloadDetailChangedZoneTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    private static IntegrationEventAuditHandler Build(InMemoryAuditEventRepository repo) =>
        new(new AuditingMessageHandler(
            repo,
            V1ResourceMap.Default,
            new FakeClock(At),
            Options.Create(new AuditMeasurementOptions()),
            NullLogger<AuditingMessageHandler>.Instance));

    private static EventMetadata Metadata() => new(Guid.CreateVersion7(), At, Fab: "munich", Actor: null);

    private static async Task<InMemoryAuditEventRepository> HandleChange(string? previous, string? url)
    {
        InMemoryAuditEventRepository repo = new();
        var message = new CameraAddressChangedV1(
            Guid.CreateVersion7(),
            "munich",
            previous!,
            url!,
            At,
            Guid.CreateVersion7(),
            Metadata());

        await Build(repo).Handle(message, default);

        return repo;
    }

    private static JsonElement Stored(InMemoryAuditEventRepository repo) =>
        JsonDocument.Parse(repo.Committed[0].Payload.Content.Value).RootElement;

    [Fact]
    public async Task Two_link_local_addresses_that_differ_only_in_their_zone_report_the_detail_change_as_unknown()
    {
        var repo = await HandleChange("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25eth1]/y");

        var raw = repo.Committed[0].Payload.Content.Value;
        var json = Stored(repo);
        json.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://[fe80::1]");
        json.GetProperty("Url").GetString().ShouldBe("rtsp://[fe80::1]");
        json.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        raw.ShouldContain("\"DetailChanged\":null", Case.Insensitive);
        raw.ShouldNotContain("eth", Case.Insensitive);
        var mapped = JsonDocument.Parse(AuditRowMapper.Map(repo.Committed[0]).Payload).RootElement;
        mapped.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1%eth0]/x", "rtsp://[fe80::1%251]/x")]
    [InlineData("rtsp://[fe80::1%1]/x", "rtsp://[fe80::1%2]/x")]
    [InlineData("rtsp://[fe80::1%25]/x", "rtsp://[fe80::1%25eth0]/x")]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25ETH0]/x")]
    [InlineData("rtsp://[fe80::1%2565th0]/x", "rtsp://[fe80::1%25eth0]/x")]
    [InlineData("rtsp://[::ffff:10.0.0.1%25eth0]/x", "rtsp://[::ffff:10.0.0.1%25eth1]/x")]
    [InlineData("rtsp://[fe80:0:0:0:0:0:0:1%25eth0]/x", "rtsp://[fe80::1%25eth1]/x")]
    [InlineData("http://[fe80::1%25eth0]/x", "http://[fe80::1%25eth1]:80/x")]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[fe80::1]/x", "rtsp://[fe80::1%25eth0]/x")]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::2]/x")]
    [InlineData("rtsp://cam/x", "rtsp://[fe80::1%25eth0]/x")]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25eth0]/x")]
    public async Task A_zone_on_either_side_reports_the_detail_change_as_unknown(
        string previous,
        string url)
    {
        var repo = await HandleChange(previous, url);

        Stored(repo).GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_same_zone_on_both_sides_with_a_different_path_reports_the_detail_change_as_unknown()
    {
        var repo = await HandleChange("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25eth0]/y");

        Stored(repo).GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1]/x", "rtsp://[fe80::1]/y", true)]
    [InlineData("rtsp://[fe80::1]/x", "rtsp://[fe80::2]/y", false)]
    [InlineData("rtsp://[fe80::1]/x", "rtsp://[fe80::1]/x", false)]
    [InlineData("rtsp://[fd00::5]/x", "rtsp://[fd00:0:0:0:0:0:0:5]/x", true)]
    [InlineData("rtsp://[::ffff:10.0.0.1]/x", "rtsp://[::ffff:10.0.0.1]/y", true)]
    public async Task An_ipv6_address_without_a_zone_keeps_its_detail_change_value(
        string previous,
        string url,
        bool expected)
    {
        var repo = await HandleChange(previous, url);

        Stored(repo).GetProperty("DetailChanged").GetBoolean().ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "rtsp://[fe80::1%25eth0]/x")]
    [InlineData("rtsp://[fe80::1%25eth0]/x", null)]
    public async Task A_null_side_against_a_zoned_side_records_no_detail_change_and_still_audits(
        string? previous,
        string? url)
    {
        var repo = await HandleChange(previous, url);

        repo.Committed.Count.ShouldBe(1);
        Stored(repo).GetProperty("DetailChanged").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_zoned_side_against_a_placeholder_side_reports_the_detail_change_as_unknown()
    {
        var repo = await HandleChange("rtsp://[fe80::1%25eth0]/x", "rtsp://cam/s?x=@");

        Stored(repo).GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_stored_origin_stays_free_of_the_zone_text()
    {
        var repo = await HandleChange("rtsp://[fe80::1%25SECRET]/x", "rtsp://[fe80::1%25eth1]/x");

        var raw = repo.Committed[0].Payload.Content.Value;
        var json = Stored(repo);
        json.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://[fe80::1]");
        json.GetProperty("Url").GetString().ShouldBe("rtsp://[fe80::1]");
        raw.ShouldNotContain("CRET", Case.Insensitive);
    }
}
