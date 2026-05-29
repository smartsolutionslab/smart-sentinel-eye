namespace SmartSentinelEye.Shared.Contracts.Identity;

/// <summary>
/// Integration event raised by Identity (spec 008) when a
/// non-human device (PLC, inference camera) has been registered
/// as a Keycloak service-account client. Carried into spec 009
/// (AuditObservability) for the device-onboarding audit log.
///
/// <para>
/// <c>DeviceType</c> matches the spec 006 <c>Source</c> wire
/// string (<c>"plc"</c> | <c>"inference"</c>).
/// </para>
/// </summary>
public sealed record DeviceRegisteredV1(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string DeviceType,
    string DeviceIdentifier,
    string Fab,
    DateTimeOffset RegisteredAt,
    EventMetadata Metadata) : IIntegrationEvent;
