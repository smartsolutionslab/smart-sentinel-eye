using System.Net;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

public sealed record DisableWebhookClientCommand(ClientId ClientId, FabIdentifier Fab)
    : ICommand<Result<RegisteredClientIdentifier, DisableWebhookClientError>>;

public abstract record DisableWebhookClientError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// Shares its code with <c>RotateWebhookClientError.WebhookClientNotFound</c>
    /// (412 there: "no client at the version you sent"). This error never
    /// reaches HTTP — only the Wolverine subscriber consumes it — so reusing
    /// the string rather than minting a second one avoids a code that exists
    /// only for a value nobody outside the process sees (spec 264 plan.md §5.2).
    /// </summary>
    public sealed record WebhookClientNotFound(string ClientId)
        : DisableWebhookClientError(
            "WEBHOOK_CLIENT_NOT_FOUND",
            $"No enrolled webhook client with clientId '{ClientId}' exists.",
            HttpStatusCode.NotFound);

    public sealed record KeycloakUnavailable(string Reason)
        : DisableWebhookClientError(
            "KEYCLOAK_UNAVAILABLE",
            $"Keycloak Admin API call failed: {Reason}",
            HttpStatusCode.BadGateway);
}

/// <summary>
/// Builds a <see cref="DisableWebhookClientError"/> as the base rather than the
/// variant. Generics are invariant, so an outcome inferred from a variant does
/// not convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class DisableWebhookClientFailures
{
    public static DisableWebhookClientError WebhookClientNotFound(string clientId) =>
        new DisableWebhookClientError.WebhookClientNotFound(clientId);

    public static DisableWebhookClientError KeycloakUnavailable(string reason) =>
        new DisableWebhookClientError.KeycloakUnavailable(reason);
}
