using System.Net;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

/// <summary>
/// Rotates a webhook integration's credential (spec 008 US5).
/// If the integration already has a Keycloak client (this is a
/// subsequent rotation) the secret is rolled; if not (first-time
/// rotation of a grandfathered spec-006 integration) the
/// Keycloak client is created and a <c>RegisteredClient</c> row
/// is added.
/// </summary>
public sealed record RotateWebhookClientCommand(
    string IntegrationName,
    FabIdentifier Fab,
    OperatorIdentifier RotatedBy)
    : ICommand<Result<WebhookClientCredentialsDto, RotateWebhookClientError>>;

public abstract record RotateWebhookClientError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidIntegrationName(string Reason)
        : RotateWebhookClientError(
            "WEBHOOK_INVALID_INPUT", Reason, HttpStatusCode.BadRequest);

    public sealed record KeycloakUnavailable(string Reason)
        : RotateWebhookClientError(
            "KEYCLOAK_UNAVAILABLE",
            $"Keycloak Admin API call failed: {Reason}",
            HttpStatusCode.BadGateway);
}
