using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

public sealed class RevokeWebhookIntegrationCommandHandler(
    IWebhookIntegrationRepository integrations,
    IClock clock,
    ILogger<RevokeWebhookIntegrationCommandHandler> logger)
    : ICommandHandler<RevokeWebhookIntegrationCommand, Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>>
{
    public async Task<Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>> HandleAsync(
        RevokeWebhookIntegrationCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fabs, name, expectedVersion) = command;

        Option<WebhookIntegration> found = await integrations.GetByNameAsync(name, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(RevokeWebhookIntegrationFailures.WebhookIntegrationNotFound(name.Value));
        }

        WebhookIntegration integration = found.Value;

        // An integration in a plant the caller does not hold is reported exactly
        // as one that never existed (#1545) — the same 404, by the same path, so
        // the answer cannot be used to enumerate another plant's integrations.
        // Not a 403: the caller addressed an integration, and "forbidden" would
        // confirm it exists.
        if (!fabs.Contains(integration.Fab))
        {
            return Failure(RevokeWebhookIntegrationFailures.WebhookIntegrationNotFound(name.Value));
        }

        // Answered before the version gate, because a repeat is not a
        // conflict. Revoke is idempotent by design (WebhookIntegration.Revoke
        // short-circuits on IsRevoked), and a client retrying after a lost
        // response still holds the pre-revoke version — gating that would
        // report a stale conflict for a change the caller themselves already
        // landed, indistinguishable from a real concurrent edit.
        //
        // Skipping the gate here costs nothing: revoke is the only command on
        // this aggregate and the row is already in its terminal state, so
        // there is no update left to lose.
        if (integration.IsRevoked)
        {
            return Success(integration.Id);
        }

        // ADR-0113 Layer 1: refuse a revoke built on a view of the integration
        // that has since moved. Checked before any mutation so nothing is
        // applied on top of stale intent.
        if (integration.Version != expectedVersion)
        {
            return Failure(RevokeWebhookIntegrationFailures.WebhookIntegrationStale(
                    name.Value, expectedVersion, integration.Version));
        }

        integration.Revoke(clock);
        await integrations.SaveAsync(cancellationToken);

        logger.WebhookIntegrationRevoked(integration.Name, integration.Id);

        return Success(integration.Id);
    }
}
