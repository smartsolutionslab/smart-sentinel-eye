namespace SmartSentinelEye.EventIngestion.Application.DTOs;

/// <summary>
/// Read-side DTO for a registered webhook integration. The token
/// hash is intentionally NOT exposed; the plaintext is shown to the
/// caller exactly once at registration time.
/// </summary>
public sealed record WebhookIntegrationDto(
    Guid Identifier,
    string Name,
    string DefaultKind,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? RevokedAt);
