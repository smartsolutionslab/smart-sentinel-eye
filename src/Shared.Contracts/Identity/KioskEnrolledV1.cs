namespace SmartSentinelEye.Shared.Contracts.Identity;

/// <summary>
/// Integration event raised by Identity (spec 008) when a kiosk
/// has been bound to a physical screen via the enrollment flow.
/// Consumed by the eventual management-web kiosk inventory.
/// </summary>
public sealed record KioskEnrolledV1(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string Fab,
    DateTimeOffset EnrolledAt) : IIntegrationEvent;
