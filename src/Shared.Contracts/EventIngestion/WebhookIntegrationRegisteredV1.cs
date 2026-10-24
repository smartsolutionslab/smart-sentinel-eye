namespace SmartSentinelEye.Shared.Contracts.EventIngestion;

/// <summary>
/// Integration event raised by EventIngestion (#3110) when an operator
/// registers a webhook integration and is handed its bearer token. AuditObservability
/// subscribes. It carries neither the token nor its hash (ADR-0185 decision 4);
/// the registering operator travels in <see cref="Metadata"/>.
/// </summary>
public sealed record WebhookIntegrationRegisteredV1(
    Guid Integration,
    string IntegrationName,
    string DefaultKind,
    DateTimeOffset RegisteredAt,
    EventMetadata Metadata) : IIntegrationEvent;
