using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public abstract record RevokeWebhookIntegrationError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record WebhookIntegrationNotFound(string Name)
        : RevokeWebhookIntegrationError(
            "WEBHOOK_INTEGRATION_NOT_FOUND",
            $"No webhook integration named '{Name}' exists.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// The caller acted on a version of the integration that has since moved
    /// on (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other contexts.
    ///
    /// <para>
    /// Genuinely raceable here, unlike Identity's disables: a rotation
    /// arriving from Identity flips the integration onto the JWT path via
    /// <c>MarkAsRotated</c>, which moves the version on a row that is still
    /// reachable by name. A revoke built on the pre-rotation read is exactly
    /// the lost update this refuses.
    /// </para>
    /// </summary>
    public sealed record WebhookIntegrationStale(string Name, int ExpectedVersion, int ActualVersion)
        : RevokeWebhookIntegrationError(
            "WEBHOOK_INTEGRATION_STALE",
            $"Webhook integration '{Name}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}
