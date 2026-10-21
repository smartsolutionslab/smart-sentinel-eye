using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.EventHandlers;

public sealed class WebhookClientRaceDisableFailedHandler(
    ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> handler,
    ILogger<WebhookClientRaceDisableFailedHandler> logger)
{
    public Task Handle(WebhookClientRaceDisableFailed message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(handler).IsNotNull();
        Ensure.That(logger).IsNotNull();

        return Task.CompletedTask;
    }
}
