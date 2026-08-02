namespace SmartSentinelEye.Identity.Application.DTOs;

/// <summary>
/// Read-side projection of a <c>RegisteredClient</c> audit row (spec 008,
/// issues #826/#827). The Keycloak <c>ClientSecret</c> is deliberately
/// absent — it is write-once and never persisted, so the list side cannot
/// and must not surface it.
/// </summary>
public sealed record RegisteredClientSummaryDto(
    Guid RegisteredClientIdentifier,
    /// <summary>
    /// Optimistic-concurrency version (ADR-0113). Echoed back via
    /// <c>If-Match</c> to disable or rotate the client. It rides the body
    /// rather than an <c>ETag</c> because Identity exposes no
    /// single-resource read to hang a response header on — the list is the
    /// only way in.
    /// </summary>
    int Version,
    string ClientId,
    string Kind,
    string Fab,
    DateTimeOffset RegisteredAt,
    Guid RegisteredBy,
    DateTimeOffset? DisabledAt,
    DateTimeOffset? LastRotatedAt);
