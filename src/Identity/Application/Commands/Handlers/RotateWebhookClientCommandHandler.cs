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
/// On both branches a second read-after-write check follows the row's own
/// commit, closing the race where a revoke lands in the window the first check
/// cannot see (plan §4, create-branch TOCTOU; spec 451, rotate branch).
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
    TimeProvider timeProvider,
    ILogger<RotateWebhookClientCommandHandler> logger)
    : ICommandHandler<RotateWebhookClientCommand, Result<WebhookClientCredentialsDto, RotateWebhookClientError>>
{
    public async Task<Result<WebhookClientCredentialsDto, RotateWebhookClientError>> HandleAsync(
        RotateWebhookClientCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (var integrationName, var fab, var rotatedBy, _) = command;

        var prepared = await PrepareAsync(command, cancellationToken);
        if (prepared.IsFailure)
        {
            return Failure(prepared.Error);
        }

        var (clientId, existing) = prepared.Value;

        var rotation = await RotateOrRegisterAsync(command, clientId, existing, cancellationToken);
        if (rotation.IsFailure)
        {
            return Failure(rotation.Error);
        }

        var disabledDuringRace = await RaceCompensation().DisableIfRevokedSinceCommitAsync(
            rotation.Value.Aggregate,
            RaceCheckFor(clientId, fab, integrationName, existing.HasValue));

        // A client this same call just disabled must not also be announced as
        // rotated — pairing a "rotated" event with a client already turned
        // off is incoherent audit state. It also means there is nothing left
        // to flush: the race-disable's own SaveAsync already committed on its
        // successful path, and the caught-concurrency path never committed at
        // all. A failed save of a client Keycloak had already disabled was
        // committed inside the race-disable (RetryAfterFailedDisableAsync).
        // Calling CommitAsync here regardless would, in production, run
        // on the SAME DbContext that call just failed on — replaying the
        // identical failing UPDATE for the entity it caught the exception
        // for, this time surfacing uncaught as a 409. The create or rotation itself still
        // succeeded, so the 200 response below is unaffected either way.
        if (!disabledDuringRace)
        {
            // From the save's commit on, nothing here may take the request token:
            // a disconnect must not skip the announcement or the flush that
            // releases the outbox, as IdempotentRequest's release does not.
            // Tell EventIngestion to flip the integration's
            // bearer-validation path from hash-compare to JWT-validate.
            await events.PublishAsync(
                new WebhookIntegrationRotatedV1(
                    integrationName, clientId.Value, clock.UtcNow,
                    Metadata: new EventMetadata(Guid.CreateVersion7(), clock.UtcNow, fab.Value, rotatedBy.Value)),
                CancellationToken.None);

            // Spec 021. The publish happens after the save, so the message
            // was captured into the outbox with nothing left to release it.
            // Reordering is not available: the announcement carries a client
            // id that only exists once Keycloak has answered, and each branch's
            // order is load-bearing for its own reasons: the rotate branch saves
            // before it rolls the secret (RollSecretAsync), the create branch
            // calls Keycloak before it saves (RegisterAsync). So the flush is
            // explicit.
            // Bounded only by Npgsql's defaults (15 s connect, 30 s command).
            await commit.CommitAsync(CancellationToken.None);

            logger.RotatedWebhookIntegration(integrationName, clientId);
        }

        return Success(CredentialsOf(rotation.Value, clientId, integrationName, fab));
    }

    private WebhookClientRaceCompensation RaceCompensation() =>
        new(
            integrationStatus,
            disableWebhookClient,
            events,
            commit,
            timeProvider,
            logger);

    private RaceCheck RaceCheckFor(
        ClientId clientId,
        FabIdentifier fab,
        string integrationName,
        bool isExistingClient) =>
        new(
            clientId,
            fab,
            integrationName,
            !isExistingClient,
            RaceLoggingFor(isExistingClient));

    private WebhookClientRaceLogging RaceLoggingFor(bool isExistingClient) =>
        isExistingClient
            ? new WebhookClientRaceLogging(
                logger.DisabledClientRotatedDuringRevokeRace,
                logger.AlreadyDisabledConcurrentlyDuringRotateRevokeRace,
                logger.CouldNotDisableClientRotatedDuringRevokeRace)
            : new WebhookClientRaceLogging(
                logger.DisabledClientCreatedDuringRevokeRace,
                logger.AlreadyDisabledConcurrentlyDuringRevokeRace,
                logger.CouldNotDisableClientCreatedDuringRevokeRace);

    // The call order is the contract: a revoked or unverifiable integration is
    // refused before any lookup, and a branch mismatch is refused before a
    // stale version, so callers keep getting the same error code for the same input.
    private async Task<Result<PreparedRotation, RotateWebhookClientError>> PrepareAsync(
        RotateWebhookClientCommand command,
        CancellationToken cancellationToken)
    {
        (var integrationName, var fab, _, var expectedVersion) = command;

        var clientIdResult = ToClientId(integrationName);
        if (clientIdResult.IsFailure)
        {
            return Failure(clientIdResult.Error);
        }

        var clientId = clientIdResult.Value;

        var revoked = await RefuseIfRevokedAsync(fab, integrationName, cancellationToken);
        if (revoked.HasValue)
        {
            return Failure(revoked.Value);
        }

        // Scoped to fab: an unscoped lookup lets a caller who names their own
        // fab (not the client's) resolve and rotate a client registered in a
        // fab they hold no access to — AS-4, spec 182.
        var existing = await clients
            .GetWithinFabAsync(fab, clientId, cancellationToken);

        var mismatch = RefuseOnVersionMismatch(existing, expectedVersion, clientId);
        if (mismatch.HasValue)
        {
            return Failure(mismatch.Value);
        }

        return Success(new PreparedRotation(clientId, existing));
    }

    private static Result<ClientId, RotateWebhookClientError> ToClientId(string integrationName)
    {
        try
        {
            return Success(ClientId.From($"webhook-{integrationName}"));
        }
        catch (ArgumentException ex)
        {
            return Failure(RotateWebhookClientFailures.InvalidIntegrationName(ex.Message));
        }
    }

    private async Task<Option<RotateWebhookClientError>> RefuseIfRevokedAsync(
        FabIdentifier fab,
        string integrationName,
        CancellationToken cancellationToken)
    {
        // Before anything else, including the local lookup in PrepareAsync and both
        // Layer-1 refusals in RefuseOnVersionMismatch: a revoked (or unverifiable) integration is refused
        // whichever precondition the caller sent, so the rotate branch's
        // aggregate.Rotate(clock) + SaveAsync can never run for it (plan §4).
        var status = await integrationStatus
            .GetStatusAsync(fab, integrationName, cancellationToken);
        switch (status)
        {
            case WebhookIntegrationStatus.Revoked:
                logger.RefusedRotationOfRevokedIntegration(integrationName, fab);
                return Option<RotateWebhookClientError>.Some(
                    RotateWebhookClientFailures.WebhookIntegrationRevoked(integrationName));
            case WebhookIntegrationStatus.Unverifiable:
                logger.RefusedRotationStatusUnavailable(integrationName, fab);
                return Option<RotateWebhookClientError>.Some(
                    RotateWebhookClientFailures.WebhookIntegrationStatusUnavailable());
        }

        return Option<RotateWebhookClientError>.None;
    }

    private static Option<RotateWebhookClientError> RefuseOnVersionMismatch(
        Option<RegisteredClientAggregate> existing,
        Option<int> expectedVersion,
        ClientId clientId)
    {
        // ADR-0113 Layer 1. The caller says which branch it intends, and a
        // mismatch is refused rather than quietly resolved the other way:
        // rotating for a caller who thought they were creating rolls a live
        // secret, and creating for a caller who mistyped the name mints a
        // Keycloak client nobody asked for.
        if (existing.HasValue != expectedVersion.HasValue)
        {
            return Option<RotateWebhookClientError>.Some(existing.HasValue
                ? RotateWebhookClientFailures.WebhookClientAlreadyExists(
                    clientId.Value, existing.Value.Version)
                : RotateWebhookClientFailures.WebhookClientNotFound(
                    clientId.Value, expectedVersion.Value));
        }

        if (existing.HasValue && existing.Value.Version != expectedVersion.Value)
        {
            return Option<RotateWebhookClientError>.Some(RotateWebhookClientFailures.WebhookClientStale(
                    clientId.Value, expectedVersion.Value, existing.Value.Version));
        }

        return Option<RotateWebhookClientError>.None;
    }

    private async Task<Result<RotationOutcome, RotateWebhookClientError>> RotateOrRegisterAsync(
        RotateWebhookClientCommand command,
        ClientId clientId,
        Option<RegisteredClientAggregate> existing,
        CancellationToken cancellationToken) =>
        existing.HasValue
            ? await RollSecretAsync(existing.Value, clientId, cancellationToken)
            : await RegisterAsync(command, clientId, cancellationToken);

    // Only the Keycloak call is guarded: a SaveAsync failure is a persistence
    // fault, not a Keycloak outage, and must propagate instead of becoming 502.
    private static async Task<Result<string, RotateWebhookClientError>> CallKeycloakAsync(
        Func<Task<string>> call)
    {
        try
        {
            return Success(await call());
        }
        catch (KeycloakClientAlreadyExistsException)
        {
            // The clientId is already taken — by another fab's webhook client
            // the fab-scoped lookup in PrepareAsync never saw. This refusal stays
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
                                   and not InvalidOperationException)
        {
            return Failure(RotateWebhookClientFailures.KeycloakUnavailable(ex.Message));
        }
    }

    private async Task<Result<RotationOutcome, RotateWebhookClientError>> RollSecretAsync(
        RegisteredClientAggregate aggregate,
        ClientId clientId,
        CancellationToken cancellationToken)
    {
        // Claim the write before rolling the secret. The version check in
        // RefuseOnVersionMismatch is a read-then-compare with no lock, so two callers
        // holding the same version both pass it; only Layer 2 (the EF
        // token on this save) picks a winner. Rolling first would let
        // the loser invalidate the winner's live credential and then
        // report 409 — the secret would belong to nobody.
        //
        // The bound replay's post-read version re-check (spec 454) depends on the
        // same order: the version bump must be saved before the secret is rolled,
        // or a replay could read the new secret and still see the old version.
        //
        // The residual failure is the inverse and much cheaper: if the
        // save commits and Keycloak then fails, LastRotatedAt is early
        // and the old secret still works, so the integration keeps
        // running and the caller retries.
        aggregate.Rotate(clock);
        await clients.SaveAsync(cancellationToken);

        var secret = await CallKeycloakAsync(async () =>
            (await keycloak.RotateClientSecretAsync(clientId.Value, cancellationToken)).ClientSecret);
        return secret.IsFailure
            ? Failure(secret.Error)
            : Success(new RotationOutcome(aggregate, secret.Value));
    }

    private async Task<Result<RotationOutcome, RotateWebhookClientError>> RegisterAsync(
        RotateWebhookClientCommand command,
        ClientId clientId,
        CancellationToken cancellationToken)
    {
        (var integrationName, var fab, var rotatedBy, _) = command;

        var secret = await CallKeycloakAsync(async () =>
            (await keycloak.CreateClientAsync(
                WebhookRepresentation(clientId, integrationName, fab),
                fabGroupPath: $"/fabs/{fab.Value}",
                cancellationToken)).ClientSecret);
        if (secret.IsFailure)
        {
            return Failure(secret.Error);
        }

        var aggregate = RegisteredClientAggregate.Register(
            clientId, ClientKind.WebhookIntegration,
            fab, rotatedBy, clock);
        clients.Add(aggregate);

        // The register branch keeps the opposite order on purpose. If
        // the row were written first and CreateClientAsync then failed,
        // GetWithinFabAsync would find it on the retry, which would
        // take the rotate branch and try to roll a secret for a
        // Keycloak client that was never created.
        await clients.SaveAsync(cancellationToken);
        return Success(new RotationOutcome(aggregate, secret.Value));
    }

    private static KeycloakClientRepresentation WebhookRepresentation(
        ClientId clientId,
        string integrationName,
        FabIdentifier fab) =>
        new(
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

    // Read after SaveAsync: the interceptor bumps the version during the
    // save, so this is the value the next rotation must send in If-Match.
    // GET /webhook-integrations serves the same value, so a caller who
    // loses this response is not locked out of rotating again.
    private static WebhookClientCredentialsDto CredentialsOf(
        RotationOutcome rotation,
        ClientId clientId,
        string integrationName,
        FabIdentifier fab) =>
        new(
            rotation.Aggregate.Id.Value,
            rotation.Aggregate.Version,
            clientId.Value,
            integrationName,
            fab.Value,
            rotation.ClientSecret);

    private readonly record struct PreparedRotation(ClientId ClientId, Option<RegisteredClientAggregate> Existing);

    private readonly record struct RotationOutcome(RegisteredClientAggregate Aggregate, string ClientSecret)
    {
        // The generated ToString prints every member, and ClientSecret is plaintext.
        public override string ToString() => nameof(RotationOutcome);
    }
}
