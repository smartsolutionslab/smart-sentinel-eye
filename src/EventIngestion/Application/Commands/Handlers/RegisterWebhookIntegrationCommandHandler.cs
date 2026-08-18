using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

public sealed class RegisterWebhookIntegrationCommandHandler(
    IWebhookIntegrationRepository integrations,
    IClock clock,
    ILogger<RegisterWebhookIntegrationCommandHandler> logger)
    : ICommandHandler<
        RegisterWebhookIntegrationCommand,
        Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>>
{
    public async Task<Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>> HandleAsync(
        RegisterWebhookIntegrationCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (WebhookIntegrationName? name, Domain.Event.FabIdentifier? fab, Domain.Event.Kind? defaultKind) = command;

        // Names stay globally unique, not per-fab: the name is the path segment
        // of POST /events/webhook/{name}, so the ingest lookup has only the name
        // to go on. The cost is that a name taken in another plant answers 409
        // rather than 201, which does disclose that it exists — recorded on
        // #1545 rather than fixed by making the ingest route ambiguous.
        Option<WebhookIntegration> existing = await integrations
            .GetByNameAsync(name, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(RegisterWebhookIntegrationFailures.WebhookIntegrationNameTaken(name.Value));
        }

        (WebhookIntegration integration, string plainToken) =
            WebhookIntegration.Register(name, fab, defaultKind, clock);

        integrations.Add(integration);
        await integrations.SaveAsync(cancellationToken);

        logger.WebhookIntegrationRegistered(integration.Name, integration.Id);

        return Success(
            new RegisterWebhookIntegrationResult(integration.Id, plainToken));
    }
}
