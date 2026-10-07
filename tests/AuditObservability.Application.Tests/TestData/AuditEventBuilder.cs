using System.Globalization;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.TestData;

internal sealed class AuditEventBuilder
{
    private DateTimeOffset occurredAt =
        DateTimeOffset.Parse("2026-05-29T08:14:33Z", CultureInfo.InvariantCulture);
    private readonly DateTimeOffset receivedAt =
        DateTimeOffset.Parse("2026-05-29T08:14:34Z", CultureInfo.InvariantCulture);
    private string? fab = "munich";
    private FabScope fabScope = FabScope.Owned;
    private string eventKind = "CameraRegisteredV1";
    private string? resourceKind = ResourceKind.Camera.Value;
    private string? resourceIdentifier = "33333333-3333-3333-3333-333333333333";
    private Guid actor = Guid.CreateVersion7();
    private string? actorUsername = "admin@munich.test";
    private Guid eventIdentifier = Guid.CreateVersion7();
    private string payload = """{"cameraIdentifier":"33333333-3333-3333-3333-333333333333"}""";

    public AuditEventBuilder WithOccurredAt(DateTimeOffset moment) { occurredAt = moment; return this; }
    public AuditEventBuilder WithFab(string? fab) { this.fab = fab; return this; }
    public AuditEventBuilder WithFabScope(FabScope scope) { fabScope = scope; return this; }
    public AuditEventBuilder WithEventKind(string kind) { eventKind = kind; return this; }
    public AuditEventBuilder WithResource(string? kind, string? identifier)
    { resourceKind = kind; resourceIdentifier = identifier; return this; }
    public AuditEventBuilder WithActor(Guid actor, string? username = "admin@munich.test")
    { this.actor = actor; actorUsername = username; return this; }
    public AuditEventBuilder WithEventIdentifier(Guid id) { eventIdentifier = id; return this; }
    public AuditEventBuilder WithPayload(string payload) { this.payload = payload; return this; }

    public AuditEventEntity Build()
    {
        V1Envelope envelope = new(
            EventTypeName: eventKind,
            OccurredAt: occurredAt,
            Fab: fab is null
                ? Option<FabIdentifier>.None
                : Option<FabIdentifier>.Some(FabIdentifier.From(fab)),
            Actor: actor == Guid.Empty
                ? ActorIdentifier.System
                : ActorIdentifier.From(actor),
            ActorUsername: actorUsername is null
                ? Option<string>.None
                : Option<string>.Some(actorUsername),
            EventIdentifier: EventIdentifier.From(eventIdentifier),
            Payload: payload,
            FabScope: fabScope);

        Option<ResourceIdentifier> identifier = resourceIdentifier is null
            ? Option<ResourceIdentifier>.None
            : Option<ResourceIdentifier>.Some(ResourceIdentifier.From(resourceIdentifier));
        V1Mapping mapping = resourceKind is null
            ? V1Mapping.Unmapped
            : new V1Mapping(Option<ResourceKind>.Some(ResourceKind.From(resourceKind)), identifier);

        return AuditEventEntity.From(envelope, mapping, new FakeClock(receivedAt));
    }
}
