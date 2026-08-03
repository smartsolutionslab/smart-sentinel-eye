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

/// <summary>
/// Builds a <see cref="RegisterWebhookIntegrationError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RegisterWebhookIntegrationFailures
{
    public static RegisterWebhookIntegrationError WebhookIntegrationNameTaken(string name) =>
        new RegisterWebhookIntegrationError.WebhookIntegrationNameTaken(name);
}
