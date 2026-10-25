using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Spec 384 / issue #2998. When either camera address redacts to the placeholder no origin was read, so the
// audit row must not claim the change stayed on one origin. Assertions are on the stored payload and the
// mapped row, never on the input text.
public class AuditPayloadDetailChangedUnknownTests
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
    public async Task A_host_move_hidden_behind_the_placeholder_reports_the_detail_change_as_unknown()
    {
        var repo = await HandleChange("rtsp://cam-line4/s?x=@", "rtsp://attacker-rtsp/s?x=@");

        var raw = repo.Committed[0].Payload.Content.Value;
        var json = Stored(repo);
        json.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        raw.ShouldContain("\"DetailChanged\":null", Case.Insensitive);
        var mapped = JsonDocument.Parse(repo.Committed[0].ToAuditRowDto().Payload).RootElement;
        mapped.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("rtsp://cam-line4/s", "rtsp://attacker-rtsp/s?x=@")]
    [InlineData("rtsp://cam-line4/s?x=@", "rtsp://cam-line4/s")]
    [InlineData("rtsp://cam／a", "rtsp://cam/a")]
    [InlineData("rtsp://cam/a", "garbage")]
    public async Task A_change_with_only_one_side_redacted_to_the_placeholder_reports_the_detail_change_as_unknown(
        string previous,
        string url)
    {
        var repo = await HandleChange(previous, url);

        var raw = repo.Committed[0].Payload.Content.Value;
        Stored(repo).GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        raw.ShouldContain("\"DetailChanged\":null", Case.Insensitive);
    }

    [Fact]
    public async Task Identical_addresses_that_redact_to_the_placeholder_report_the_detail_change_as_unknown()
    {
        var repo = await HandleChange("rtsp://cam-line4/s?x=@", "rtsp://cam-line4/s?x=@");

        Stored(repo).GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_redelivered_message_already_holding_the_placeholder_reports_the_detail_change_as_unknown()
    {
        var repo = await HandleChange(UrlRedaction.Unparseable, UrlRedaction.Unparseable);

        var json = Stored(repo);
        json.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(null, "rtsp://cam-line4/s?x=@")]
    [InlineData("rtsp://cam-line4/s?x=@", null)]
    public async Task A_null_side_with_a_placeholder_other_side_records_no_detail_change_and_still_audits(
        string? previous,
        string? url)
    {
        var repo = await HandleChange(previous, url);

        repo.Committed.Count.ShouldBe(1);
        Stored(repo).GetProperty("DetailChanged").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Two_read_origins_that_are_equal_with_differing_raw_values_still_report_a_detail_change()
    {
        var repo = await HandleChange("rtsp://cam/a", "rtsp://cam/b");

        Stored(repo).GetProperty("DetailChanged").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Two_read_origins_that_differ_still_report_no_detail_change()
    {
        var repo = await HandleChange("rtsp://cam/a", "rtsp://other/a");

        Stored(repo).GetProperty("DetailChanged").GetBoolean().ShouldBeFalse();
    }
}
