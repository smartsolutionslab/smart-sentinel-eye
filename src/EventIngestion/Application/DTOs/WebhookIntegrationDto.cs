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
/// <param name="Fab">
/// The plant the integration belongs to (#1545). Carried unlike
/// <c>DeadLetterDto</c>'s, which is deliberately absent: every row here is in a
/// fab the caller holds and the column is never null, so it discloses nothing —
/// and a multi-fab admin cannot otherwise tell two plants' integrations apart.
/// </param>
public sealed record WebhookIntegrationDto(
    Guid Identifier,
    int Version,
    string Name,
    string Fab,
    string DefaultKind,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? RevokedAt);
