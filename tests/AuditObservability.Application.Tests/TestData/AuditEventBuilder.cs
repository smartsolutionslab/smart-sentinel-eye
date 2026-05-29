using System.Globalization;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.TestData;

internal sealed class AuditEventBuilder
{
    private DateTimeOffset _occurredAt =
        DateTimeOffset.Parse("2026-05-29T08:14:33Z", CultureInfo.InvariantCulture);
    private readonly DateTimeOffset _receivedAt =
        DateTimeOffset.Parse("2026-05-29T08:14:34Z", CultureInfo.InvariantCulture);
    private string? _fab = "munich";
    private string _eventKind = "CameraRegisteredV1";
    private string? _resourceKind = ResourceKind.Camera.Value;
    private string? _resourceIdentifier = "33333333-3333-3333-3333-333333333333";
    private Guid _actor = Guid.CreateVersion7();
    private string? _actorUsername = "admin@munich.test";
    private Guid _eventIdentifier = Guid.CreateVersion7();
    private string _payload = """{"cameraIdentifier":"33333333-3333-3333-3333-333333333333"}""";

    public AuditEventBuilder WithOccurredAt(DateTimeOffset moment) { _occurredAt = moment; return this; }
    public AuditEventBuilder WithFab(string? fab) { _fab = fab; return this; }
    public AuditEventBuilder WithEventKind(string kind) { _eventKind = kind; return this; }
    public AuditEventBuilder WithResource(string? kind, string? identifier)
    { _resourceKind = kind; _resourceIdentifier = identifier; return this; }
    public AuditEventBuilder WithActor(Guid actor, string? username = "admin@munich.test")
    { _actor = actor; _actorUsername = username; return this; }
    public AuditEventBuilder WithEventIdentifier(Guid id) { _eventIdentifier = id; return this; }
    public AuditEventBuilder WithPayload(string payload) { _payload = payload; return this; }

    public AuditEventEntity Build()
    {
        V1Envelope envelope = new(
            EventTypeName: _eventKind,
            OccurredAt: _occurredAt,
            Fab: _fab is null
                ? Option<FabIdentifier>.None
                : Option<FabIdentifier>.Some(FabIdentifier.From(_fab)),
            Actor: _actor == Guid.Empty
                ? ActorIdentifier.System
                : ActorIdentifier.From(_actor),
            ActorUsername: _actorUsername is null
                ? Option<string>.None
                : Option<string>.Some(_actorUsername),
            EventIdentifier: EventIdentifier.From(_eventIdentifier),
            Payload: _payload);

        Option<ResourceIdentifier> identifier = _resourceIdentifier is null
            ? Option<ResourceIdentifier>.None
            : Option<ResourceIdentifier>.Some(ResourceIdentifier.From(_resourceIdentifier));
        V1Mapping mapping = _resourceKind is null
            ? V1Mapping.Unmapped
            : new V1Mapping(Option<ResourceKind>.Some(ResourceKind.From(_resourceKind)), identifier);

        return AuditEventEntity.From(envelope, mapping, new FakeClock(_receivedAt));
    }
}
