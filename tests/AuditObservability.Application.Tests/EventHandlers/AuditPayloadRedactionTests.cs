using System.Globalization;
using System.Text;
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
/// Spec 377 (#2977): a camera URL reaches the audit payload as its origin only. The real
/// <see cref="IntegrationEventAuditHandler"/> runs over the in-memory repository; the stored string,
/// the API DTO built from the stored row, and the parsed JSON are each inspected. The marker is ASCII
/// so default JSON escaping cannot hide a leak.
/// </summary>
public class AuditPayloadRedactionTests
{
    private const string Marker = "SECRET";

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-30T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset OccurredAt =
        DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    private static IntegrationEventAuditHandler Build(InMemoryAuditEventRepository repo) =>
        new(new AuditingMessageHandler(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            Options.Create(new AuditMeasurementOptions()),
            NullLogger<AuditingMessageHandler>.Instance));

    private static EventMetadata Metadata() =>
        new(Guid.CreateVersion7(), OccurredAt, Fab: "munich", Actor: null);

    private static async Task<StoredRow> RegisteredAsync(string? url)
    {
        InMemoryAuditEventRepository repo = new();
        CameraRegisteredV1 message = new(
            Guid.CreateVersion7(), "north-gate", url!, OccurredAt, Guid.CreateVersion7(), Metadata());

        await Build(repo).Handle(message, default);

        return StoredRow.From(repo);
    }

    private static async Task<StoredRow> ChangedAsync(string? previousUrl, string? url)
    {
        InMemoryAuditEventRepository repo = new();
        CameraAddressChangedV1 message = new(
            Guid.CreateVersion7(), "munich", previousUrl!, url!, OccurredAt, Guid.CreateVersion7(), Metadata());

        await Build(repo).Handle(message, default);

        return StoredRow.From(repo);
    }

    [Theory]
    [InlineData("rtsp://cam/stream?token=SECRET")]
    [InlineData("rtsp://cam/SECRET/path")]
    [InlineData("rtsp://cam/stream#SECRET")]
    [InlineData("rtsp://cam?SECRET")]
    public async Task A_registered_camera_url_is_stored_as_its_origin(string url)
    {
        var row = await RegisteredAsync(url);

        row.Json.GetProperty("Url").GetString().ShouldBe("rtsp://cam");
        row.Raw.ShouldNotContain(Marker);
    }

    [Fact]
    public async Task A_registration_keeps_every_other_payload_field()
    {
        InMemoryAuditEventRepository repo = new();
        var camera = Guid.CreateVersion7();
        var registeredBy = Guid.CreateVersion7();
        CameraRegisteredV1 message = new(
            camera, "north-gate", "rtsp://cam/stream?token=SECRET", OccurredAt, registeredBy, Metadata());

        await Build(repo).Handle(message, default);

        var json = StoredRow.From(repo).Json;
        json.GetProperty("Camera").GetGuid().ShouldBe(camera);
        json.GetProperty("Name").GetString().ShouldBe("north-gate");
        json.GetProperty("RegisteredAt").GetDateTimeOffset().ShouldBe(OccurredAt);
        json.GetProperty("RegisteredBy").GetGuid().ShouldBe(registeredBy);
        json.GetProperty("Metadata").GetProperty("Fab").GetString().ShouldBe("munich");
    }

    [Fact]
    public async Task A_registration_records_the_size_of_the_redacted_payload()
    {
        var row = await RegisteredAsync("rtsp://cam/stream?token=SECRET");

        row.Size.ShouldBe(Encoding.UTF8.GetByteCount(row.Raw));
        row.Raw.ShouldNotContain(Marker);
    }

    [Theory]
    [InlineData("not a url SECRET")]
    [InlineData("/relative/SECRET")]
    [InlineData("")]
    public async Task An_unusable_registration_url_is_stored_as_the_placeholder(string url)
    {
        var row = await RegisteredAsync(url);

        row.Json.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        row.Raw.ShouldNotContain(Marker);
    }

    [Fact]
    public async Task A_null_registration_url_is_stored_as_null_and_the_row_is_still_written()
    {
        var row = await RegisteredAsync(null);

        row.Json.GetProperty("Url").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_address_change_within_one_origin_stores_both_origins_and_a_detail_change()
    {
        var row = await ChangedAsync("rtsp://cam/a?token=SECRETOLD", "rtsp://cam/b?token=SECRETNEW");

        row.Json.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://cam");
        row.Json.GetProperty("Url").GetString().ShouldBe("rtsp://cam");
        row.Json.GetProperty("DetailChanged").GetBoolean().ShouldBeTrue();
        row.Raw.ShouldNotContain(Marker);
    }

    [Fact]
    public async Task An_address_change_across_origins_shows_the_move_and_no_detail_change()
    {
        var row = await ChangedAsync("rtsp://10.0.5.12/h264?token=SECRET", "rtsp://10.0.5.44/h264");

        row.Json.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://10.0.5.12");
        row.Json.GetProperty("Url").GetString().ShouldBe("rtsp://10.0.5.44");
        row.Json.GetProperty("DetailChanged").GetBoolean().ShouldBeFalse();
        row.Raw.ShouldNotContain(Marker);
    }

    [Fact]
    public async Task An_address_change_to_an_identical_url_records_no_detail_change()
    {
        var row = await ChangedAsync("rtsp://cam/x?token=SECRET", "rtsp://cam/x?token=SECRET");

        row.Json.GetProperty("DetailChanged").GetBoolean().ShouldBeFalse();
        row.Raw.ShouldNotContain(Marker);
    }

    [Theory]
    [InlineData(null, "rtsp://cam/x?token=SECRET")]
    [InlineData("rtsp://cam/x?token=SECRET", null)]
    public async Task An_address_change_with_a_null_side_records_no_detail_change_and_still_audits(
        string? previousUrl,
        string? url)
    {
        var row = await ChangedAsync(previousUrl, url);

        row.Json.GetProperty("DetailChanged").GetBoolean().ShouldBeFalse();
        row.Raw.ShouldNotContain(Marker);
    }

    [Fact]
    public async Task An_address_change_keeps_every_other_payload_field()
    {
        InMemoryAuditEventRepository repo = new();
        var camera = Guid.CreateVersion7();
        var changedBy = Guid.CreateVersion7();
        CameraAddressChangedV1 message = new(
            camera, "munich", "rtsp://cam/a?token=SECRET", "rtsp://cam/b", OccurredAt, changedBy, Metadata());

        await Build(repo).Handle(message, default);

        var row = StoredRow.From(repo);
        row.Json.GetProperty("Camera").GetGuid().ShouldBe(camera);
        row.Json.GetProperty("Fab").GetString().ShouldBe("munich");
        row.Json.GetProperty("ChangedAt").GetDateTimeOffset().ShouldBe(OccurredAt);
        row.Json.GetProperty("ChangedBy").GetGuid().ShouldBe(changedBy);
        row.Raw.ShouldNotContain(Marker);
    }

    [Fact]
    public async Task The_audit_api_row_for_a_registration_carries_no_credential()
    {
        InMemoryAuditEventRepository repo = new();
        CameraRegisteredV1 message = new(
            Guid.CreateVersion7(), "north-gate", "rtsp://cam/stream?token=SECRET", OccurredAt,
            Guid.CreateVersion7(), Metadata());
        await Build(repo).Handle(message, default);

        var dto = AuditRowMapper.Map(repo.Committed[0]);

        dto.Payload.ShouldNotContain(Marker);
        JsonDocument.Parse(dto.Payload).RootElement.GetProperty("Url").GetString().ShouldBe("rtsp://cam");
    }

    [Fact]
    public async Task The_audit_api_row_for_an_address_change_carries_no_credential()
    {
        InMemoryAuditEventRepository repo = new();
        CameraAddressChangedV1 message = new(
            Guid.CreateVersion7(), "munich", "rtsp://cam/SECRET/a", "rtsp://cam/SECRET/b", OccurredAt,
            Guid.CreateVersion7(), Metadata());
        await Build(repo).Handle(message, default);

        var dto = AuditRowMapper.Map(repo.Committed[0]);

        dto.Payload.ShouldNotContain(Marker);
        JsonDocument.Parse(dto.Payload).RootElement.GetProperty("DetailChanged").GetBoolean().ShouldBeTrue();
    }

    private sealed record StoredRow(string Raw, JsonElement Json, int Size)
    {
        public static StoredRow From(InMemoryAuditEventRepository repo)
        {
            repo.Committed.Count.ShouldBe(1);
            var payload = repo.Committed[0].Payload;
            var raw = payload.Content.Value;
            return new StoredRow(raw, JsonDocument.Parse(raw).RootElement.Clone(), payload.Size.Value);
        }
    }
}
