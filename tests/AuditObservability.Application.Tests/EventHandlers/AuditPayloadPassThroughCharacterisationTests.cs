using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

/// <summary>
/// Characterisation (spec 377 FR-006, ADR-0144), captured green on unmodified <c>develop</c>: an
/// audited message with no camera URL is stored byte for byte as
/// <c>JsonSerializer.Serialize(message, message.GetType())</c>. Must pass unmodified after the
/// redaction lands, so the 22 untouched types cannot drift (number or escape formatting included).
/// </summary>
public class AuditPayloadPassThroughCharacterisationTests
{
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

    [Fact]
    public async Task A_camera_rename_is_stored_exactly_as_the_serializer_writes_it()
    {
        InMemoryAuditEventRepository repo = new();
        CameraRenamedV1 message = new(
            Guid.CreateVersion7(), "munich", "north-gate", "line-4 \"inlet\" <east>", OccurredAt,
            Guid.CreateVersion7(), Metadata());

        await Build(repo).Handle(message, default);

        repo.Committed[0].Payload.Content.Value.ShouldBe(JsonSerializer.Serialize(message, message.GetType()));
    }

    [Fact]
    public async Task An_ingested_fab_event_is_stored_exactly_as_the_serializer_writes_it()
    {
        InMemoryAuditEventRepository repo = new();
        FabEventIngestedV1 message = new(
            Guid.CreateVersion7(), "munich", "plc", "press-7", "alarm",
            OccurredAt, OccurredAt, """{"temp":71.50,"note":"café <hot>"}""", Metadata());

        await Build(repo).Handle(message, default);

        repo.Committed[0].Payload.Content.Value.ShouldBe(JsonSerializer.Serialize(message, message.GetType()));
    }

    [Fact]
    public async Task A_published_overlay_with_decimals_is_stored_exactly_as_the_serializer_writes_it()
    {
        InMemoryAuditEventRepository repo = new();
        OverlayRevisionPublishedV3 message = new(
            Guid.CreateVersion7(),
            3,
            "north-gate",
            [
                new OverlayElementV3("Text", "#ff0000", 0.100m, 0.2500m, 0.5m, 0.125m, "Alarm <1>", 24),
                new OverlayElementV3("Box", "#00ff00", 0m, 1.0m, 0.3333m, 0.6667m, null, null),
            ],
            OccurredAt,
            Guid.CreateVersion7(),
            Metadata());

        await Build(repo).Handle(message, default);

        repo.Committed[0].Payload.Content.Value.ShouldBe(JsonSerializer.Serialize(message, message.GetType()));
    }
}
