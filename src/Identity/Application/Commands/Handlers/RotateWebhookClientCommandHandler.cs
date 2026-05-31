using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

/// <summary>
/// Rotates a webhook integration's bearer to a Keycloak
/// service-account client (spec 008 US5 / FR-014).
///
/// <para>
/// Hard-cut migration (FR-016): the first time the admin rotates,
/// we create the Keycloak client + the local
/// <see cref="RegisteredClientAggregate"/> row. Subsequent
/// rotations just roll the secret. In both cases the resulting
/// <see cref="WebhookIntegrationRotatedV1"/> is published so
/// EventIngestion flips the bearer-validation path.
/// </para>
/// </summary>
public sealed class RotateWebhookClientCommandHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak,
    IEventBus events,
    IClock clock,
    ILogger<RotateWebhookClientCommandHandler> logger)
    : ICommandHandler<
        RotateWebhookClientCommand,
        Result<WebhookClientCredentialsDto, RotateWebhookClientError>>
{
    public async Task<Result<WebhookClientCredentialsDto, RotateWebhookClientError>> HandleAsync(
        RotateWebhookClientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (integrationName, fab, rotatedBy) = command;

        ClientId clientId;
        try
        {
            clientId = ClientId.From($"webhook-{integrationName}");
        }
        catch (ArgumentException ex)
        {
            return Result<WebhookClientCredentialsDto, RotateWebhookClientError>.Failure(
                new RotateWebhookClientError.InvalidIntegrationName(ex.Message));
        }

        Option<RegisteredClientAggregate> existing = await clients
            .GetByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);

        string clientSecret;
        RegisteredClientAggregate aggregate;
        try
        {
            if (existing.HasValue)
            {
                KeycloakClientCredentials rolled = await keycloak
                    .RotateClientSecretAsync(clientId.Value, cancellationToken)
                    .ConfigureAwait(false);
                clientSecret = rolled.ClientSecret;
                aggregate = existing.Value;
                aggregate.Rotate(clock);
            }
            else
            {
                KeycloakClientRepresentation representation = new(
                    ClientId: clientId.Value,
                    Name: $"Webhook {integrationName}",
                    ServiceAccountsEnabled: true,
                    StandardFlowEnabled: false,
                    DirectAccessGrantsEnabled: false,
                    PublicClient: false,
                    DefaultClientScopes: KeycloakScopeBundles.WebhookIntegration,
                    OptionalClientScopes: Array.Empty<string>(),
                    Attributes: new Dictionary<string, string>
                    {
                        ["sse.kind"] = "webhook",
                        ["sse.integrationName"] = integrationName,
                        ["sse.fab"] = fab.Value,
                    });
                KeycloakClientCredentials credentials = await keycloak.CreateClientAsync(
                    representation,
                    fabGroupPath: $"/fabs/{fab.Value}",
                    cancellationToken).ConfigureAwait(false);
                clientSecret = credentials.ClientSecret;

                aggregate = RegisteredClientAggregate.Register(
                    clientId, ClientKind.WebhookIntegration,
                    fab, rotatedBy, clock);
                clients.Add(aggregate);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not InvalidOperationException)
        {
            return Result<WebhookClientCredentialsDto, RotateWebhookClientError>.Failure(
                new RotateWebhookClientError.KeycloakUnavailable(ex.Message));
        }

        await clients.SaveAsync(cancellationToken).ConfigureAwait(false);

        // Tell EventIngestion to flip the integration's
        // bearer-validation path from hash-compare to JWT-validate.
        await events.PublishAsync(
            new WebhookIntegrationRotatedV1(
                integrationName, clientId.Value, clock.UtcNow,
                Metadata: new EventMetadata(Guid.CreateVersion7(), clock.UtcNow, fab.Value, rotatedBy.Value)),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Rotated webhook integration '{IntegrationName}' to clientId '{ClientId}'.",
            integrationName, clientId);

        return Result<WebhookClientCredentialsDto, RotateWebhookClientError>.Success(
            new WebhookClientCredentialsDto(
                aggregate.Id.Value,
                clientId.Value,
                integrationName,
                fab.Value,
                clientSecret));
    }
}
