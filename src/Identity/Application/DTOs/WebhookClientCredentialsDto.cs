namespace SmartSentinelEye.Identity.Application.DTOs;

/// <summary>
/// Response shape from <c>POST /webhook-integrations/{name}/rotate</c>.
/// </summary>
public sealed record WebhookClientCredentialsDto(
    Guid RegisteredClientIdentifier,
    /// <summary>
    /// The client's version *after* this rotation — what the caller must send
    /// in <c>If-Match</c> to rotate again (ADR-0113).
    ///
    /// <para>
    /// A mutating response carries it because nothing else can: webhook
    /// clients are <see cref="Domain.RegisteredClient.ClientKind.WebhookIntegration"/>,
    /// and both list endpoints filter to devices and kiosks, so there is no
    /// read path to source the version from. Without it here the endpoint
    /// would 409 permanently from the third rotation on.
    /// </para>
    /// </summary>
    int Version,
    string ClientId,
    string IntegrationName,
    string Fab,
    string ClientSecret);
