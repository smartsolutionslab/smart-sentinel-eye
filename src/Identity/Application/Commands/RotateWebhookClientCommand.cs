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
/// <param name="ExpectedVersion">
/// Which of the two operations the caller intends (ADR-0113).
/// <see cref="Option{T}.None"/> asserts the client does not exist yet and
/// only the register branch is acceptable; a value asserts it exists at
/// exactly that version and only the rotate branch is acceptable. The
/// handler refuses the mismatch either way rather than silently taking the
/// other branch.
/// </param>
public sealed record RotateWebhookClientCommand(
    string IntegrationName,
    FabIdentifier Fab,
    OperatorIdentifier RotatedBy,
    Option<int> ExpectedVersion)
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

    /// <summary>
    /// The caller acted on a version of the webhook client that has since
    /// moved on (ADR-0113 Layer 1).
    /// </summary>
    public sealed record WebhookClientStale(string ClientId, int ExpectedVersion, int ActualVersion)
        : RotateWebhookClientError(
            "WEBHOOK_CLIENT_STALE",
            $"Webhook client '{ClientId}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// The caller sent <c>If-None-Match: *</c> — "create it, it does not
    /// exist" — but it does. Refused rather than rotated: taking the other
    /// branch would roll a live secret for a caller who believed they were
    /// creating something.
    /// </summary>
    public sealed record WebhookClientAlreadyExists(string ClientId, int ActualVersion)
        : RotateWebhookClientError(
            "WEBHOOK_CLIENT_ALREADY_EXISTS",
            $"Webhook client '{ClientId}' already exists (version {ActualVersion}). Send If-Match with that version to rotate it.",
            HttpStatusCode.PreconditionFailed);

    /// <summary>
    /// The caller sent <c>If-Match</c> — "it exists at version N" — but no
    /// client exists. Refused rather than registered: creating a Keycloak
    /// client for a caller who mistyped the integration name would mint a
    /// live credential nobody asked for.
    /// </summary>
    public sealed record WebhookClientNotFound(string ClientId, int ExpectedVersion)
        : RotateWebhookClientError(
            "WEBHOOK_CLIENT_NOT_FOUND",
            $"No webhook client '{ClientId}' exists to be at version {ExpectedVersion}. Send If-None-Match: * to create it.",
            HttpStatusCode.PreconditionFailed);

    /// <summary>
    /// The Keycloak clientId derived from the requested integration name is
    /// already taken — by this caller's own fab, or by another fab's webhook
    /// client the caller's fab-scoped lookup above never saw. Deliberately
    /// generic, mirroring <see cref="SmartSentinelEye.ServiceDefaults.Persistence.UniqueConstraintExceptionHandler"/>:
    /// naming the clientId here would additionally tell the caller which fab
    /// already owns this integration name. This closes that message-text
    /// leak only — the 409-vs-200 status code distinction itself is still a
    /// cross-fab existence oracle; closing it would need fab-qualifying the
    /// Keycloak clientId namespace, which is out of scope here and tracked
    /// in a separate follow-up.
    /// </summary>
    public sealed record WebhookClientNameConflict()
        : RotateWebhookClientError(
            "RESOURCE_ALREADY_EXISTS",
            "Something with that name or key already exists. Choose a different one — "
            + "retrying this request unchanged will be refused again.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// EventIngestion reports this integration revoked (spec 318, issue
    /// #2628). Checked before the local lookup and both Layer-1 branches, so
    /// a revoked integration is refused whichever precondition the caller
    /// sent, and the rotate branch's secret roll can never run for it.
    /// </summary>
    public sealed record WebhookIntegrationRevoked(string IntegrationName)
        : RotateWebhookClientError(
            "WEBHOOK_INTEGRATION_REVOKED",
            $"Webhook integration '{IntegrationName}' has been revoked and cannot be rotated. "
            + "Register a new integration under a different name.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// EventIngestion's revocation state could not be confirmed — unreachable,
    /// a non-2xx answer, a resilience timeout, or a malformed body (spec 318
    /// §4). Failing closed: a rotation this context cannot verify as safe is
    /// refused rather than allowed, mirroring <see cref="KeycloakUnavailable"/>
    /// for the handler's other upstream dependency. The message is fixed text
    /// rather than an echoed exception, because the reason is logged and an
    /// upstream's error text has no business in this response.
    /// </summary>
    public sealed record WebhookIntegrationStatusUnavailable()
        : RotateWebhookClientError(
            "WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE",
            "Could not confirm with EventIngestion that this webhook integration is not revoked, "
            + "so it was not rotated. The current credential still works; retry later.",
            HttpStatusCode.BadGateway);
}

/// <summary>
/// Builds a <see cref="RotateWebhookClientError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RotateWebhookClientFailures
{
    public static RotateWebhookClientError InvalidIntegrationName(string reason) =>
        new RotateWebhookClientError.InvalidIntegrationName(reason);

    public static RotateWebhookClientError KeycloakUnavailable(string reason) =>
        new RotateWebhookClientError.KeycloakUnavailable(reason);

    public static RotateWebhookClientError WebhookClientStale(string clientId, int expectedVersion, int actualVersion) =>
        new RotateWebhookClientError.WebhookClientStale(clientId, expectedVersion, actualVersion);

    public static RotateWebhookClientError WebhookClientAlreadyExists(string clientId, int actualVersion) =>
        new RotateWebhookClientError.WebhookClientAlreadyExists(clientId, actualVersion);

    public static RotateWebhookClientError WebhookClientNotFound(string clientId, int expectedVersion) =>
        new RotateWebhookClientError.WebhookClientNotFound(clientId, expectedVersion);

    public static RotateWebhookClientError WebhookClientNameConflict() =>
        new RotateWebhookClientError.WebhookClientNameConflict();

    public static RotateWebhookClientError WebhookIntegrationRevoked(string integrationName) =>
        new RotateWebhookClientError.WebhookIntegrationRevoked(integrationName);

    public static RotateWebhookClientError WebhookIntegrationStatusUnavailable() =>
        new RotateWebhookClientError.WebhookIntegrationStatusUnavailable();
}
