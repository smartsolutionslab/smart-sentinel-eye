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
    public async Task Handle(WebhookClientRaceDisableFailed message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (integrationName, fabName) = message;

        var fab = ParseFab(fabName, integrationName);
        if (!fab.HasValue)
        {
            return;
        }

        await WebhookClientDisabling.DisableOrThrowForRetryAsync(
            handler,
            logger,
            integrationName,
            fab.Value,
            cancellationToken);
    }

    private Option<FabIdentifier> ParseFab(string fabName, string integrationName)
    {
        if (string.IsNullOrWhiteSpace(fabName))
        {
            logger.RevocationFabInvalid(exception: null, integrationName, "(none)");
            return Option<FabIdentifier>.None;
        }

        try
        {
            return Option<FabIdentifier>.Some(FabIdentifier.From(fabName));
        }
        catch (ArgumentException ex)
        {
            logger.RevocationFabInvalid(ex, integrationName, fabName);
            return Option<FabIdentifier>.None;
        }
    }
}
