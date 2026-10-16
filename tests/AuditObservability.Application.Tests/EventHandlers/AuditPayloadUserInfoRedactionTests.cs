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

// Spec 381 / issue #2993. A camera address whose userinfo the parser read as host and port reaches the
// audit payload as the placeholder, never as an origin that still holds the credential. Assertions are on
// the stored payload and the mapped row, never on the input text.
public class AuditPayloadUserInfoRedactionTests
{
    private const string Credential = "operator:2468";
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
    public async Task Registering_a_camera_whose_userinfo_was_cut_short_stores_the_placeholder()
    {
        InMemoryAuditEventRepository repo = new();
        var message = new CameraRegisteredV1(
            Guid.CreateVersion7(),
            "n",
            "rtsp://operator:2468#!@10.0.0.5:554/h264",
            At,
            Guid.CreateVersion7(),
            Metadata());

        await Build(repo).Handle(message, default);

        var raw = repo.Committed[0].Payload.Content.Value;
        JsonDocument.Parse(raw).RootElement.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        raw.ShouldNotContain(Credential, Case.Insensitive);
        AuditRowMapper.Map(repo.Committed[0]).Payload.ShouldNotContain(Credential, Case.Insensitive);
    }

    [Fact]
    public async Task Changing_between_two_cut_short_addresses_stores_the_placeholder_and_reports_the_detail_change_as_unknown()
    {
        InMemoryAuditEventRepository repo = new();
        var message = new CameraAddressChangedV1(
            Guid.CreateVersion7(),
            "munich",
            "rtsp://operator:2468#@cam/s",
            "rtsp://operator:1357#@cam/s",
            At,
            Guid.CreateVersion7(),
            Metadata());

        await Build(repo).Handle(message, default);

        var raw = repo.Committed[0].Payload.Content.Value;
        var json = JsonDocument.Parse(raw).RootElement;
        json.GetProperty("PreviousUrl").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("Url").GetString().ShouldBe(UrlRedaction.Unparseable);
        json.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        raw.ShouldNotContain(Credential, Case.Insensitive);
        AuditRowMapper.Map(repo.Committed[0]).Payload.ShouldNotContain(Credential, Case.Insensitive);
    }

    [Fact]
    public async Task Changing_between_addresses_with_a_proper_userinfo_still_stores_their_origins()
    {
        InMemoryAuditEventRepository repo = new();
        var message = new CameraAddressChangedV1(
            Guid.CreateVersion7(),
            "munich",
            "rtsp://user:pass@cam/x",
            "rtsp://cam2/x",
            At,
            Guid.CreateVersion7(),
            Metadata());

        await Build(repo).Handle(message, default);

        var json = JsonDocument.Parse(repo.Committed[0].Payload.Content.Value).RootElement;
        json.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://cam");
        json.GetProperty("Url").GetString().ShouldBe("rtsp://cam2");
        json.GetProperty("DetailChanged").GetBoolean().ShouldBeFalse();
    }
}
