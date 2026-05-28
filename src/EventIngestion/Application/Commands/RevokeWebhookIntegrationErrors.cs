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
}
