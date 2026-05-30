using System.Text.Json;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.EventHandlers;

/// <summary>
/// The audit bus subscriber (spec 009 FR-005 / US1). One audit row is
/// written per delivery of every <c>*V1</c> integration event.
///
/// <para>
/// The audit <em>logic</em> is generic: <see cref="AuditAsync"/> reads the
/// uniform <see cref="EventMetadata"/> envelope (ADR-0102) off any
/// <see cref="IIntegrationEvent"/>, serialises the message to the JSON
/// payload, and writes through <see cref="AuditingMessageHandler"/>.
/// Wolverine has no interface/polymorphic handler discovery (handlers are
/// matched by concrete type, which is what drives RabbitMQ conventional-
/// routing listener creation), so each concrete event needs a thin
/// <c>Handle</c> entry point that delegates to the shared method. The
/// architecture test <c>Every_integration_event_has_an_audit_handler</c>
/// fails if a new <c>*V1</c> is added without an entry here, so coverage
/// can't silently regress.
/// </para>
/// </summary>
public sealed class IntegrationEventAuditHandler(AuditingMessageHandler auditing)
{
    public Task Handle(CameraRegisteredV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(FabEventIngestedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(DeviceRegisteredV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(KioskEnrolledV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(WebhookIntegrationRotatedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(LayoutRevisionArchivedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(LayoutRevisionPublishedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(OverlayHighlightRequestedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(OverlayRevisionArchivedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(OverlayRevisionPublishedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(StreamHealthChangedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(SystemVariableArchivedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(SystemVariableDefinedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(SystemVariableValueChangedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(SystemVariableValueRequestedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);
    public Task Handle(AuditChunkArchivedV1 message, CancellationToken cancellationToken) => AuditAsync(message, cancellationToken);

    private Task AuditAsync(IIntegrationEvent message, CancellationToken cancellationToken)
    {
        EventMetadata meta = message.Metadata;
        V1Envelope envelope = new(
            EventTypeName: message.GetType().Name,
            OccurredAt: meta.OccurredAt,
            Fab: meta.Fab is null
                ? Option<FabIdentifier>.None
                : Option<FabIdentifier>.Some(FabIdentifier.From(meta.Fab)),
            Actor: meta.Actor is { } actor && actor != Guid.Empty
                ? ActorIdentifier.From(actor)
                : ActorIdentifier.System,
            ActorUsername: Option<string>.None,
            EventIdentifier: EventIdentifier.From(meta.EventIdentifier),
            Payload: JsonSerializer.Serialize(message, message.GetType()));

        return auditing.HandleAsync(message.GetType(), message, envelope, cancellationToken);
    }
}
