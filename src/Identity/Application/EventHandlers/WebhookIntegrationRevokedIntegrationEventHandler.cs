using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that translates the cross-context
/// <c>WebhookIntegrationRevokedV1</c> into a <see cref="DisableWebhookClientCommand"/>
/// (spec 264, #2206). Shape mirrors <c>CameraRetiredIntegrationEventHandler</c>
/// (StreamDistribution): a message that can never succeed is dropped rather
/// than retried forever; a retryable failure is thrown so Wolverine redelivers
/// it. Discovered by convention (ADR-0088) — no hand-written listener or route.
/// </summary>
public sealed class WebhookIntegrationRevokedIntegrationEventHandler(
    ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> handler,
    ILogger<WebhookIntegrationRevokedIntegrationEventHandler> logger)
{
    public async Task Handle(WebhookIntegrationRevokedV1 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (integrationName, _, metadata) = message;

        Option<FabIdentifier> fab = ParseFab(metadata, integrationName);
        if (!fab.HasValue)
        {
            return;
        }

        ClientId clientId;
        try
        {
            clientId = ClientId.From($"webhook-{integrationName}");
        }
        catch (ArgumentException ex)
        {
            logger.InvalidRevocationName(ex, integrationName);
            return;
        }

        Result<RegisteredClientIdentifier, DisableWebhookClientError> result = await handler.HandleAsync(
            new DisableWebhookClientCommand(clientId, fab.Value), cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error is DisableWebhookClientError.WebhookClientNotFound)
            {
                logger.NoWebhookClientToDisable(integrationName, fab.Value);
                return;
            }

            logger.WebhookClientDisableFailed(integrationName, result.Error.Code, result.Error.Message);

            // Wolverine treats an exception as a retry signal. A Keycloak outage
            // must not be swallowed — swallowing it would leave the client
            // enabled with nothing left to retry.
            throw new InvalidOperationException(
                $"DisableWebhookClientCommand failed for '{integrationName}': {result.Error.Code}");
        }
    }

    /// <summary>
    /// A null or unparsable fab is refused rather than resolved by name alone:
    /// this handler disables a security-relevant Keycloak client. <c>metadata</c>
    /// is read through <c>?.</c> even though <see cref="EventMetadata"/> is
    /// declared non-nullable on the contract — a deserialized message missing
    /// <c>Metadata</c> yields a genuine <see langword="null"/> at runtime
    /// regardless of that annotation (mirrors
    /// <c>WebhookIntegrationRotatedV1Handler.ParseFab</c>).
    /// </summary>
    private Option<FabIdentifier> ParseFab(EventMetadata metadata, string integrationName)
    {
        if (string.IsNullOrWhiteSpace(metadata?.Fab))
        {
            logger.RevocationFabInvalid(exception: null, integrationName, "(none)");
            return Option<FabIdentifier>.None;
        }

        try
        {
            return Option<FabIdentifier>.Some(FabIdentifier.From(metadata.Fab));
        }
        catch (ArgumentException ex)
        {
            logger.RevocationFabInvalid(ex, integrationName, metadata.Fab);
            return Option<FabIdentifier>.None;
        }
    }
}
