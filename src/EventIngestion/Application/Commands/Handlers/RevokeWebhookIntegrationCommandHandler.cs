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

        Option<WebhookIntegration> found = await integrations.GetByNameAsync(command.Name, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(RevokeWebhookIntegrationFailures.WebhookIntegrationNotFound(command.Name.Value));
        }

        WebhookIntegration integration = found.Value;

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
        if (integration.Version != command.ExpectedVersion)
        {
            return Failure(RevokeWebhookIntegrationFailures.WebhookIntegrationStale(
                    command.Name.Value, command.ExpectedVersion, integration.Version));
        }

        integration.Revoke(clock);
        await integrations.SaveAsync(cancellationToken);

        logger.WebhookIntegrationRevoked(integration.Name, integration.Id);

        return Success(integration.Id);
    }
}
