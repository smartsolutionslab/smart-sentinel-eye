namespace SmartSentinelEye.EventIngestion.Application.DTOs;

/// <summary>
/// Read-side DTO for a registered webhook integration. The token
/// hash is intentionally NOT exposed; the plaintext is shown to the
/// caller exactly once at registration time.
/// </summary>
/// <param name="Version">
/// Optimistic-concurrency version (ADR-0113). Echoed back via <c>If-Match</c>
/// to revoke. It rides the body rather than an <c>ETag</c> because this
/// context exposes no single-resource read — the list is the only way in.
/// </param>
public sealed record WebhookIntegrationDto(
    Guid Identifier,
    int Version,
    string Name,
    string DefaultKind,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? RevokedAt);
