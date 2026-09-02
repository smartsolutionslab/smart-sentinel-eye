using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

/// <summary>
/// Spec 053 — the measurement apparatus, and the fact that it is normally absent.
///
/// <para>
/// <b>This puts equipment on a path every change in the system passes through.</b>
/// The stamps answer one question about one requirement; they are not part of the
/// product. So the default is off, and the default is <i>tested</i> rather than
/// intended — a default nobody checks is a default that drifts, and this one
/// drifting means every audit row in every deployment carries measurement
/// timestamps nobody asked for.
/// </para>
/// </summary>
public class AuditMeasurementSwitchTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:14:33Z", CultureInfo.InvariantCulture);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    private static AuditingMessageHandler Build(
        InMemoryAuditEventRepository repository, AuditMeasurementOptions options) =>
        new(repository,
            V1ResourceMap.Default,
            new FakeClock(Now),
            Options.Create(options),
            NullLogger<AuditingMessageHandler>.Instance);

    private static CameraRegisteredV1 AnEvent() =>
        new(Guid.CreateVersion7(), "north-gate", "rtsp://example/cam", Now, Guid.CreateVersion7(), Metadata);

    private static V1Envelope EnvelopeFor(CameraRegisteredV1 message) =>
        new(
            EventTypeName: nameof(CameraRegisteredV1),
            OccurredAt: message.RegisteredAt,
            Fab: Option<FabIdentifier>.None,
            Actor: ActorIdentifier.System,
            ActorUsername: Option<string>.None,
            EventIdentifier: EventIdentifier.From(Guid.CreateVersion7()),
            Payload: System.Text.Json.JsonSerializer.Serialize(message));

    private static async Task<AuditEventEntity> WriteOneAsync(AuditMeasurementOptions options)
    {
        InMemoryAuditEventRepository repository = new();
        CameraRegisteredV1 message = AnEvent();

        await Build(repository, options)
            .HandleAsync(typeof(CameraRegisteredV1), message, EnvelopeFor(message), CancellationToken.None);

        return repository.Committed.ShouldHaveSingleItem();
    }

    /// <summary>
    /// **The default is off, asserted on a row rather than on the option.**
    ///
    /// <para>
    /// Reading the property back would only confirm the field's initial value.
    /// What matters is that a row written through the ordinary path — with
    /// nothing configured, which is every deployment — carries no measurement
    /// stamp at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task With_nothing_configured_a_row_carries_no_measurement_stamp()
    {
        AuditEventEntity row = await WriteOneAsync(new AuditMeasurementOptions());

        row.HandlerEnteredAt.ShouldBeNull(
            "the apparatus sits on a path every change passes through; absent is what an ordinary row looks like");
    }

    /// <summary>
    /// Stated separately from the row assertion so that a change to the field's
    /// default fails here, and a change to how the handler reads it fails above.
    /// The two can break independently.
    /// </summary>
    [Fact]
    public void The_option_itself_defaults_to_off()
    {
        new AuditMeasurementOptions().RecordIngestBreakdown.ShouldBeFalse();
    }

    /// <summary>
    /// The other side of the switch — otherwise "no stamp" would be satisfied by
    /// a handler that never stamps at all, and the whole apparatus could be
    /// missing without a single test noticing.
    /// </summary>
    [Fact]
    public async Task With_the_switch_on_a_row_carries_the_stamp()
    {
        AuditEventEntity row = await WriteOneAsync(new AuditMeasurementOptions { RecordIngestBreakdown = true });

        row.HandlerEnteredAt.ShouldNotBeNull();
    }

    /// <summary>
    /// **Order is the property, not presence.**
    ///
    /// <para>
    /// The handler is entered before it stamps its arrival time, so the first
    /// must not be after the second. Reversed, the difference between them is a
    /// negative part of a breakdown — which would either be reported as nonsense
    /// or, worse, quietly reduce the total it belongs to.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_entry_stamp_is_not_after_the_arrival_stamp()
    {
        AuditEventEntity row = await WriteOneAsync(new AuditMeasurementOptions { RecordIngestBreakdown = true });

        row.HandlerEnteredAt!.Value.ShouldBeLessThanOrEqualTo(
            row.ReceivedAt,
            "entering the handler happens before stamping arrival; reversed, this part of the breakdown is negative");
    }

    /// <summary>
    /// Nothing else about the row changes. The switch divides a span; it does not
    /// alter what the audit trail records, and a reviewer should be able to see
    /// that asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task The_switch_changes_nothing_else_about_the_row()
    {
        AuditEventEntity off = await WriteOneAsync(new AuditMeasurementOptions());
        AuditEventEntity on = await WriteOneAsync(new AuditMeasurementOptions { RecordIngestBreakdown = true });

        on.EventKind.ShouldBe(off.EventKind);
        on.Fab.ShouldBe(off.Fab);
        on.Actor.ShouldBe(off.Actor);
        on.SchemaVersion.ShouldBe(off.SchemaVersion);
        on.Payload.Size.ShouldBe(off.Payload.Size);
    }
}
