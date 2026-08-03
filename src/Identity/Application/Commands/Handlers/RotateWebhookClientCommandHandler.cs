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
        Ensure.That(command).IsNotNull();
        (string? integrationName, FabIdentifier? fab, OperatorIdentifier rotatedBy, Option<int> expectedVersion) = command;

        ClientId clientId;
        try
        {
            clientId = ClientId.From($"webhook-{integrationName}");
        }
        catch (ArgumentException ex)
        {
            return Failure(RotateWebhookClientFailures.InvalidIntegrationName(ex.Message));
        }

        Option<RegisteredClientAggregate> existing = await clients
            .GetByClientIdAsync(clientId, cancellationToken);

        // ADR-0113 Layer 1. The caller says which branch it intends, and a
        // mismatch is refused rather than quietly resolved the other way:
        // rotating for a caller who thought they were creating rolls a live
        // secret, and creating for a caller who mistyped the name mints a
        // Keycloak client nobody asked for.
        if (existing.HasValue != expectedVersion.HasValue)
        {
            return Failure(existing.HasValue
                ? RotateWebhookClientFailures.WebhookClientAlreadyExists(
                    clientId.Value, existing.Value.Version)
                : RotateWebhookClientFailures.WebhookClientNotFound(
                    clientId.Value, expectedVersion.Value));
        }

        if (existing.HasValue && existing.Value.Version != expectedVersion.Value)
        {
            return Failure(RotateWebhookClientFailures.WebhookClientStale(
                    clientId.Value, expectedVersion.Value, existing.Value.Version));
        }

        string clientSecret;
        RegisteredClientAggregate aggregate;
        try
        {
            if (existing.HasValue)
            {
                // Claim the write before rolling the secret. The version check
                // above is a read-then-compare with no lock, so two callers
                // holding the same version both pass it; only Layer 2 (the EF
                // token on this save) picks a winner. Rolling first would let
                // the loser invalidate the winner's live credential and then
                // report 409 — the secret would belong to nobody.
                //
                // The residual failure is the inverse and much cheaper: if the
                // save commits and Keycloak then fails, LastRotatedAt is early
                // and the old secret still works, so the integration keeps
                // running and the caller retries.
                aggregate = existing.Value;
                aggregate.Rotate(clock);
                await clients.SaveAsync(cancellationToken);

                KeycloakClientCredentials rolled = await keycloak
                    .RotateClientSecretAsync(clientId.Value, cancellationToken);
                clientSecret = rolled.ClientSecret;
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
                    cancellationToken);
                clientSecret = credentials.ClientSecret;

                aggregate = RegisteredClientAggregate.Register(
                    clientId, ClientKind.WebhookIntegration,
                    fab, rotatedBy, clock);
                clients.Add(aggregate);

                // The register branch keeps the opposite order on purpose. If
                // the row were written first and CreateClientAsync then failed,
                // GetByClientIdAsync would find it on the retry, which would
                // take the rotate branch and try to roll a secret for a
                // Keycloak client that was never created.
                await clients.SaveAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not InvalidOperationException)
        {
            return Failure(RotateWebhookClientFailures.KeycloakUnavailable(ex.Message));
        }

        // Tell EventIngestion to flip the integration's
        // bearer-validation path from hash-compare to JWT-validate.
        await events.PublishAsync(
            new WebhookIntegrationRotatedV1(
                integrationName, clientId.Value, clock.UtcNow,
                Metadata: new EventMetadata(Guid.CreateVersion7(), clock.UtcNow, fab.Value, rotatedBy.Value)),
            cancellationToken);

        logger.RotatedWebhookIntegration(integrationName, clientId);

        // Read after SaveAsync: the interceptor bumps the version during the
        // save, so this is the value the next rotation must send in If-Match.
        // GET /webhook-integrations serves the same value, so a caller who
        // loses this response is not locked out of rotating again.
        return Success(
            new WebhookClientCredentialsDto(
                aggregate.Id.Value,
                aggregate.Version,
                clientId.Value,
                integrationName,
                fab.Value,
                clientSecret));
    }
}
