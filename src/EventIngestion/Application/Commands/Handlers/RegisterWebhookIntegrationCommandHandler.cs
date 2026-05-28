using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

public sealed class RegisterWebhookIntegrationCommandHandler(
    IWebhookIntegrationRepository integrations,
    IClock clock,
    ILogger<RegisterWebhookIntegrationCommandHandler> log)
    : ICommandHandler<
        RegisterWebhookIntegrationCommand,
        Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>>
{
    public async Task<Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>> HandleAsync(
        RegisterWebhookIntegrationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<WebhookIntegration> existing = await integrations
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>.Failure(
                new RegisterWebhookIntegrationError.WebhookIntegrationNameTaken(command.Name.Value));
        }

        (WebhookIntegration integration, string plainToken) =
            WebhookIntegration.Register(command.Name, command.DefaultKind, clock);

        integrations.Add(integration);
        await integrations.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Registered webhook integration '{Name}' ({Identifier}).",
            integration.Name, integration.Id);

        return Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>.Success(
            new RegisterWebhookIntegrationResult(integration.Id, plainToken));
    }
}
