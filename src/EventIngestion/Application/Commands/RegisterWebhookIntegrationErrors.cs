using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public abstract record RegisterWebhookIntegrationError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record WebhookIntegrationNameTaken(string Name)
        : RegisterWebhookIntegrationError(
            "WEBHOOK_INTEGRATION_NAME_TAKEN",
            $"A webhook integration named '{Name}' already exists.",
            HttpStatusCode.Conflict);
}
