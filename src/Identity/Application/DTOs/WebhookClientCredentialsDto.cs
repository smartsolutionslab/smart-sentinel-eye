namespace SmartSentinelEye.Identity.Application.DTOs;

/// <summary>
/// Response shape from <c>POST /webhook-integrations/{name}/rotate</c>.
/// </summary>
/// <param name="Version">
/// The client's version *after* this rotation — what the caller must send in
/// <c>If-Match</c> to rotate again (ADR-0113). Carried here so the common
/// case needs no extra round-trip; <c>GET /webhook-integrations</c> serves
/// the same value for a caller who no longer has this response.
/// </param>
public sealed record WebhookClientCredentialsDto(
    Guid RegisteredClientIdentifier,
    int Version,
    string ClientId,
    string IntegrationName,
    string Fab,
    string ClientSecret);
