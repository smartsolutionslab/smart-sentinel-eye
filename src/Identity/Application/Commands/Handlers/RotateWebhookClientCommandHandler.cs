using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
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
///
/// <para>
/// Spec 318 (#2628): before either branch runs, <see cref="integrationStatus"/>
/// is asked whether EventIngestion considers this integration revoked, and a
/// <c>Revoked</c> or <c>Unverifiable</c> answer refuses the rotation outright.
/// On the create branch specifically, a second read-after-write check follows
/// the new row's own commit, closing the race where a revoke lands in the
/// window the first check cannot see (plan §4, create-branch TOCTOU).
/// </para>
/// </summary>
public sealed class RotateWebhookClientCommandHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak,
    IWebhookIntegrationStatusLookup integrationStatus,
    IEventBus events,
    ITransactionalCommit commit,
    IClock clock,
    ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> disableWebhookClient,
    ILogger<RotateWebhookClientCommandHandler> logger)
    : ICommandHandler<RotateWebhookClientCommand, Result<WebhookClientCredentialsDto, RotateWebhookClientError>>
{
    public async Task<Result<WebhookClientCredentialsDto, RotateWebhookClientError>> HandleAsync(
        RotateWebhookClientCommand command,
        CancellationToken cancellationToken)
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

        // Before anything else, including the local lookup and both Layer-1
        // branches below: a revoked (or unverifiable) integration is refused
        // whichever precondition the caller sent, so the rotate branch's
        // aggregate.Rotate(clock) + SaveAsync can never run for it (plan §4).
        WebhookIntegrationStatus status = await integrationStatus
            .GetStatusAsync(fab, integrationName, cancellationToken);
        switch (status)
        {
            case WebhookIntegrationStatus.Revoked:
                logger.RefusedRotationOfRevokedIntegration(integrationName, fab);
                return Failure(RotateWebhookClientFailures.WebhookIntegrationRevoked(integrationName));
            case WebhookIntegrationStatus.Unverifiable:
                logger.RefusedRotationStatusUnavailable(integrationName, fab);
                return Failure(RotateWebhookClientFailures.WebhookIntegrationStatusUnavailable());
        }

        // Scoped to fab: an unscoped lookup lets a caller who names their own
        // fab (not the client's) resolve and rotate a client registered in a
        // fab they hold no access to — AS-4, spec 182.
        Option<RegisteredClientAggregate> existing = await clients
            .GetWithinFabAsync(fab, clientId, cancellationToken);
        bool isCreate = !existing.HasValue;

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
                    DefaultClientScopes:
                    [
                        .. KeycloakScopeBundles.WebhookIntegration,
                        KeycloakScopeBundles.AudienceScope,
                        KeycloakScopeBundles.GroupsScope,
                    ],
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
                // GetWithinFabAsync would find it on the retry, which would
                // take the rotate branch and try to roll a secret for a
                // Keycloak client that was never created.
                await clients.SaveAsync(cancellationToken);
            }
        }
        catch (KeycloakClientAlreadyExistsException)
        {
            // The clientId is already taken — by another fab's webhook client
            // this fab-scoped lookup above never saw. This refusal stays
            // generic (see WebhookClientNameConflict) to avoid naming the
            // clientId in the message. That closes the message-text leak
            // only: the 409-vs-200 status code itself is still a cross-fab
            // existence oracle, and RegisterDeviceCommandHandler /
            // EnrollKioskCommandHandler have the identical property via the
            // same unscoped GetByClientIdAsync lookup — see the tracked
            // follow-up on this handler's own namespace-collision oracle.
            return Failure(RotateWebhookClientFailures.WebhookClientNameConflict());
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not InvalidOperationException
                                   // A Layer-2 loser's DbUpdateConcurrencyException must propagate
                                   // to ConcurrencyConflictExceptionHandler's 409
                                   // AGGREGATE_VERSION_STALE (ADR-0113), not be swallowed here as
                                   // 502 KEYCLOAK_UNAVAILABLE.
                                   and not DbUpdateConcurrencyException)
        {
            return Failure(RotateWebhookClientFailures.KeycloakUnavailable(ex.Message));
        }

        bool disabledDuringRace = isCreate
            && await DisableIfRevokedSinceCommitAsync(clientId, fab, integrationName, cancellationToken);

        // Spec 318 (#2628) S2: a client this same call just disabled must not
        // also be announced as rotated — that pairs a "rotated" event with a
        // client already turned off, which is incoherent audit state. The
        // create itself still succeeded, so the 200 response below is
        // unaffected.
        if (!disabledDuringRace)
        {
            // Tell EventIngestion to flip the integration's
            // bearer-validation path from hash-compare to JWT-validate.
            await events.PublishAsync(
                new WebhookIntegrationRotatedV1(
                    integrationName, clientId.Value, clock.UtcNow,
                    Metadata: new EventMetadata(Guid.CreateVersion7(), clock.UtcNow, fab.Value, rotatedBy.Value)),
                cancellationToken);
        }

        // Spec 021. The publish happens after the save, so the message was
        // captured into the outbox with nothing left to release it. Reordering
        // is not available: the announcement carries a client id that only
        // exists once Keycloak has answered, and the save-then-Keycloak order
        // above is load-bearing for its own reasons. So the flush is explicit.
        await commit.CommitAsync(cancellationToken);

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

    /// <summary>
    /// Spec 318 (#2628) create-branch TOCTOU: a revoke can land in the window
    /// between the pre-flight status read above and this branch's own
    /// <c>SaveAsync</c> committing the new row. EventIngestion's
    /// <c>WebhookIntegrationRevokedV1</c> disable (spec 264) has no row to
    /// disable yet at that moment, so it is dropped, and without this the
    /// client would stay live and enabled indefinitely. <c>Unverifiable</c> is
    /// treated the same as <c>Revoked</c> here, matching the pre-flight check's
    /// own fail-closed philosophy a few lines above: a status this handler
    /// cannot confirm is not one it leaves a live credential attached to.
    ///
    /// <para>
    /// A second read-after-write check, not a distributed lock: the create has
    /// already committed, so a positive answer here calls
    /// <c>DisableWebhookClientCommand</c>'s own handler directly rather than a
    /// duplicated inline copy of its Keycloak-then-aggregate-then-save steps —
    /// the two paths share one implementation and cannot drift apart. That
    /// call is synchronous inside this request, not a Wolverine-redelivered
    /// message, so it does not inherit the async disable's own retry-on-
    /// redelivery; it is still best-effort, same as before. A failure to
    /// disable is logged, not surfaced, because the create itself genuinely
    /// succeeded and must not be reported as a failure on that account. A
    /// <see cref="DbUpdateConcurrencyException"/> from the inner handler's own
    /// save means the asynchronous <c>WebhookIntegrationRevokedV1</c> handler
    /// won this exact race first — the row is disabled either way, so that is
    /// logged as "already disabled concurrently", not retried as a conflict.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the client ended up disabled — by this call or
    /// by the concurrent async handler — so the caller can skip announcing the
    /// rotation for a client that is no longer enabled (S2).
    /// </returns>
    private async Task<bool> DisableIfRevokedSinceCommitAsync(
        ClientId clientId,
        FabIdentifier fab,
        string integrationName,
        CancellationToken cancellationToken)
    {
        WebhookIntegrationStatus postCommitStatus = await integrationStatus
            .GetStatusAsync(fab, integrationName, cancellationToken);
        if (postCommitStatus is not (WebhookIntegrationStatus.Revoked or WebhookIntegrationStatus.Unverifiable))
        {
            return false;
        }

        Result<RegisteredClientIdentifier, DisableWebhookClientError> result;
        try
        {
            result = await disableWebhookClient.HandleAsync(
                new DisableWebhookClientCommand(clientId, fab), cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.AlreadyDisabledConcurrentlyDuringRevokeRace(integrationName, clientId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.CouldNotDisableClientCreatedDuringRevokeRace(ex, integrationName, clientId);
            return false;
        }

        if (result.IsFailure)
        {
            logger.CouldNotDisableClientCreatedDuringRevokeRace(
                new InvalidOperationException(
                    $"DisableWebhookClientCommand failed for '{integrationName}': {result.Error.Code}"),
                integrationName, clientId);
            return false;
        }

        logger.DisabledClientCreatedDuringRevokeRace(integrationName, clientId);
        return true;
    }
}
