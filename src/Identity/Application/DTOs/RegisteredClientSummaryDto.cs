namespace SmartSentinelEye.Identity.Application.DTOs;

/// <summary>
/// Read-side projection of a <c>RegisteredClient</c> audit row (spec 008,
/// issues #826/#827). The Keycloak <c>ClientSecret</c> is deliberately
/// absent — it is write-once and never persisted, so the list side cannot
/// and must not surface it.
/// </summary>
public sealed record RegisteredClientSummaryDto(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string Kind,
    string Fab,
    DateTimeOffset RegisteredAt,
    Guid RegisteredBy,
    DateTimeOffset? DisabledAt,
    DateTimeOffset? LastRotatedAt);
