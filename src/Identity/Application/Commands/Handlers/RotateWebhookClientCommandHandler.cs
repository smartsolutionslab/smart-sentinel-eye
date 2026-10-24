using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.EventHandlers;
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
    TimeProvider timeProvider,
    ILogger<RotateWebhookClientCommandHandler> logger)
    : ICommandHandler<RotateWebhookClientCommand, Result<WebhookClientCredentialsDto, RotateWebhookClientError>>
{
    // The standard resilience handler's total request timeout: long enough for the
    // status lookup or the Keycloak disable to exhaust its own retries, short
    // enough not to wait on a Keycloak that never answers.
    private static readonly TimeSpan CompensationBound = TimeSpan.FromSeconds(30);

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

        var disabledDuringRace = !existing.HasValue
            && await DisableIfRevokedSinceCommitAsync(
                rotation.Value.Aggregate,
                clientId,
                fab,
                integrationName);

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
        // for, this time surfacing uncaught as a 409. The create itself still
        // succeeded, so the 200 response below is unaffected either way.
        if (!disabledDuringRace)
        {
            // From the create's commit on, nothing here may take the request token:
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
        }

        logger.RotatedWebhookIntegration(integrationName, clientId);

        return Success(CredentialsOf(rotation.Value, clientId, integrationName, fab));
    }

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

    /// <summary>
    /// Create-branch TOCTOU: a revoke can land in the window
    /// between the pre-flight status read in <c>RefuseIfRevokedAsync</c> and this branch's own
    /// <c>SaveAsync</c> committing the new row. EventIngestion's
    /// <c>WebhookIntegrationRevokedV1</c> disable (spec 264) has no row to
    /// disable yet at that moment, so it is dropped, and without this the
    /// client would stay live and enabled indefinitely. <c>Unverifiable</c> is
    /// treated the same as <c>Revoked</c> here, matching the pre-flight check's
    /// own fail-closed philosophy in <c>RefuseIfRevokedAsync</c>: a status this handler
    /// cannot confirm is not one it leaves a live credential attached to.
    ///
    /// <para>
    /// A second read-after-write check, not a distributed lock: the create has
    /// already committed, so a positive answer here calls
    /// <c>DisableWebhookClientCommand</c>'s own handler directly rather than a
    /// duplicated inline copy of its Keycloak-then-aggregate-then-save steps —
    /// the two paths share one implementation and cannot drift apart. That
    /// call is synchronous inside this request, so it does not inherit the
    /// async disable's own retry-on-redelivery. A failure to disable is
    /// logged and handed to the same retry ladder by publishing
    /// <see cref="WebhookClientRaceDisableFailed"/> into the outbox, which the
    /// caller's single commit flushes (or, once Keycloak has disabled the client,
    /// is committed in place); it is not surfaced, because the create
    /// itself genuinely succeeded and must not be reported as a failure on
    /// that account. A
    /// <see cref="DbUpdateConcurrencyException"/> from the inner handler's own
    /// save means the asynchronous <c>WebhookIntegrationRevokedV1</c> handler
    /// won this exact race first — the row is disabled either way, so that is
    /// logged as "already disabled concurrently", not retried as a conflict.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the client ended up disabled — by this call or
    /// by the concurrent async handler — so the caller can skip announcing the
    /// rotation for a client that is no longer enabled.
    /// </returns>
    private async Task<bool> DisableIfRevokedSinceCommitAsync(
        RegisteredClientAggregate aggregate,
        ClientId clientId,
        FabIdentifier fab,
        string integrationName)
    {
        // Unlinked from the request token on purpose: the create has committed,
        // so a client disconnect must not abandon the compensation. The bound
        // is the only thing allowed to stop it, and an expiry fails closed.
        using CancellationTokenSource bound = new(CompensationBound, timeProvider);
        return await DisableIfRevokedAsync(aggregate, clientId, fab, integrationName, bound.Token);
    }

    private async Task<bool> DisableIfRevokedAsync(
        RegisteredClientAggregate aggregate,
        ClientId clientId,
        FabIdentifier fab,
        string integrationName,
        CancellationToken cancellationToken)
    {
        Result<RegisteredClientIdentifier, DisableWebhookClientError> result;
        var disabling = false;
        try
        {
            var postCommitStatus = await integrationStatus
                .GetStatusAsync(fab, integrationName, cancellationToken);
            if (postCommitStatus is not (WebhookIntegrationStatus.Revoked or WebhookIntegrationStatus.Unverifiable))
            {
                return false;
            }

            disabling = true;
            result = await disableWebhookClient.HandleAsync(
                new DisableWebhookClientCommand(clientId, fab), cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The entity this save attempted stays tracked as Modified with
            // its stale, pre-conflict Version — EF does not discard it on
            // failure. Nothing here calls SaveChanges on this context again
            // (the caller skips its own final commit for exactly this
            // reason), but detaching anyway means a later save on this scope
            // cannot replay the identical failing UPDATE by accident.
            Detach(ex);

            logger.AlreadyDisabledConcurrentlyDuringRevokeRace(integrationName, clientId);
            return true;
        }
        catch (OperationCanceledException ex)
        {
            // Deliberately every cancellation, not only the bound's: no request
            // token reaches these awaits, so one that is not the bound's is an
            // internal timeout (HttpClient.Timeout), and a failed disable all the same.
            logger.CouldNotDisableClientCreatedDuringRevokeRace(ex, integrationName, clientId);
            return await RetryAfterFailedDisableAsync(aggregate, integrationName, fab);
        }
        catch (Exception ex) when (disabling)
        {
            // A failed save leaves its entries tracked as Modified; the commit
            // that follows on this same context (the caller's or the helper's)
            // would replay the failing UPDATE.
            if (ex is DbUpdateException updateException)
            {
                Detach(updateException);
            }

            logger.CouldNotDisableClientCreatedDuringRevokeRace(ex, integrationName, clientId);
            return await RetryAfterFailedDisableAsync(aggregate, integrationName, fab);
        }

        return await AcknowledgeRaceDisableAsync(result, integrationName, fab, clientId);
    }

    /// <summary>
    /// The shared tail of every exit where the inner disable did not finish
    /// cleanly: publishes the idempotent retry message and decides who commits.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the client ended up disabled — the caller must
    /// neither announce the rotation nor commit, because this helper has already
    /// committed; <see langword="false"/> if Keycloak never disabled it, so the
    /// caller continues to announce and commit.
    /// </returns>
    private async Task<bool> RetryAfterFailedDisableAsync(
        RegisteredClientAggregate aggregate,
        string integrationName,
        FabIdentifier fab)
    {
        if (aggregate.DisabledAt is null)
        {
            await PublishRetryAsync(integrationName, fab);
            return false;
        }

        // Keycloak has disabled the client and only the row's save failed or was
        // cut short. The aggregate is the same tracked instance (GetWithinFabAsync
        // is a tracking query on the scoped context), so after any failure other
        // than a DbUpdateException (a cancellation, or e.g. a TimeoutException)
        // the entry is still tracked and this commit saves the disabled row. After
        // a DbUpdateException its entries are normally detached (Entries can be
        // empty), so the commit flushes the outbox. If cancellation landed inside the
        // transaction commit the outcome is unknown and the row may stay enabled,
        // so the idempotent retry is published too: it finds a disabled row
        // NotFound and treats the client as already disabled. Report disabled, so
        // no rotation is announced.
        await PublishRetryAsync(integrationName, fab);
        await commit.CommitAsync(CancellationToken.None);
        return true;
    }

    private static void Detach(DbUpdateException exception)
    {
        foreach (var entry in exception.Entries)
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task PublishRetryAsync(
        string integrationName,
        FabIdentifier fab) =>
        await events.PublishAsync(
            new WebhookClientRaceDisableFailed(integrationName, fab.Value),
            CancellationToken.None);

    private async Task<bool> AcknowledgeRaceDisableAsync(
        Result<RegisteredClientIdentifier, DisableWebhookClientError> result,
        string integrationName,
        FabIdentifier fab,
        ClientId clientId)
    {
        if (result.IsFailure)
        {
            // The asynchronous WebhookIntegrationRevokedV1 handler can beat
            // this re-check to the disable outright: its own save commits
            // first, and the fab-scoped lookup this inner handler re-runs
            // filters out Disabled rows at the database level, so it finds
            // nothing rather than a conflict. Mirrors
            // WebhookIntegrationRevokedIntegrationEventHandler's own
            // NotFound handling for the identical reason — the client is
            // disabled either way.
            if (result.Error is DisableWebhookClientError.WebhookClientNotFound)
            {
                logger.AlreadyDisabledConcurrentlyDuringRevokeRace(integrationName, clientId);
                return true;
            }

            logger.CouldNotDisableClientCreatedDuringRevokeRace(
                new InvalidOperationException(
                    $"DisableWebhookClientCommand failed for '{integrationName}': {result.Error.Code}"),
                integrationName, clientId);
            await PublishRetryAsync(integrationName, fab);
            return false;
        }

        logger.DisabledClientCreatedDuringRevokeRace(integrationName, clientId);
        return true;
    }
}
