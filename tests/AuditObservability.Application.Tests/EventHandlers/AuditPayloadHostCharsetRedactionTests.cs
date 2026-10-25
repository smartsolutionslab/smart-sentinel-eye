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

// Spec 379 / issue #2989. A camera address whose host carries a non-ASCII character reaches the audit
// payload as the placeholder, never as a lower-cased origin that still holds the secret. Marker checks are
// case-insensitive: ToOrigin lower-cases the host.
public class AuditPayloadHostCharsetRedactionTests
{
    private const string Marker = "SECRET";
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    private static IntegrationEventAuditHandler Build(InMemoryAuditEventRepository repo) =>
        new(new AuditingMessageHandler(
            repo,
            V1ResourceMap.Default,
            new FakeClock(At),
            Options.Create(new AuditMeasurementOptions()),
            NullLogger<AuditingMessageHandler>.Instance));

    private static EventMetadata Metadata() => new(Guid.CreateVersion7(), At, Fab: "munich", Actor: null);

    [Fact]
    public async Task Registering_a_camera_with_a_look_alike_delimiter_in_its_host_stores_the_placeholder()
    {
        InMemoryAuditEventRepository repo = new();
        var message = new CameraRegisteredV1(
            Guid.CreateVersion7(),
            "n",
            "rtsp://cam／token＝" + Marker,
            At,
            Guid.CreateVersion7(),
            Metadata());

        await Build(repo).Handle(message, default);

        var raw = repo.Committed[0].Payload.Content.Value;
        JsonDocument.Parse(raw).RootElement.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        raw.ShouldNotContain(Marker, Case.Insensitive);
        repo.Committed[0].ToAuditRowDto().Payload.ShouldNotContain(Marker, Case.Insensitive);
    }

    [Fact]
    public async Task Changing_the_address_to_one_with_a_fullwidth_userinfo_stores_the_placeholder_for_both_fields()
    {
        InMemoryAuditEventRepository repo = new();
        var message = new CameraAddressChangedV1(
            Guid.CreateVersion7(),
            "munich",
            "rtsp://user：" + Marker + "＠cam:554",
            "rtsp://" + Marker + "＠cam/x",
            At,
            Guid.CreateVersion7(),
            Metadata());

        await Build(repo).Handle(message, default);

        var raw = repo.Committed[0].Payload.Content.Value;
        var json = JsonDocument.Parse(raw).RootElement;
        json.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        raw.ShouldNotContain(Marker, Case.Insensitive);
        repo.Committed[0].ToAuditRowDto().Payload.ShouldNotContain(Marker, Case.Insensitive);
    }

    [Fact]
    public async Task Changing_between_two_different_non_ascii_addresses_reports_the_detail_change_as_unknown()
    {
        InMemoryAuditEventRepository repo = new();
        var message = new CameraAddressChangedV1(
            Guid.CreateVersion7(),
            "munich",
            "rtsp://cam／a＝" + Marker + "1",
            "rtsp://cam／a＝" + Marker + "2",
            At,
            Guid.CreateVersion7(),
            Metadata());

        await Build(repo).Handle(message, default);

        var raw = repo.Committed[0].Payload.Content.Value;
        var json = JsonDocument.Parse(raw).RootElement;
        json.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        raw.ShouldNotContain(Marker, Case.Insensitive);
    }
}
