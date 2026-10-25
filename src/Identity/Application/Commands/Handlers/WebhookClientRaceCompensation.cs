using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

internal sealed class WebhookClientRaceCompensation(
    IWebhookIntegrationStatusLookup integrationStatus,
    ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> disableWebhookClient,
    IEventBus events,
    ITransactionalCommit commit,
    TimeProvider timeProvider,
    ILogger logger)
{
    // The standard resilience handler's total request timeout: long enough for the
    // status lookup or the Keycloak disable to exhaust its own retries, short
    // enough not to wait on a Keycloak that never answers.
    private static readonly TimeSpan CompensationBound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// TOCTOU on both branches: a revoke can land in the window between the
    /// pre-flight status read in <c>RefuseIfRevokedAsync</c> and this request's
    /// own <c>SaveAsync</c>. On the create branch EventIngestion's
    /// <c>WebhookIntegrationRevokedV1</c> disable (spec 264) has no row to
    /// disable yet at that moment, so it is dropped, and without this the
    /// client would stay live and enabled indefinitely; on the rotate branch it
    /// can disable the row before the rotation is announced. For a new client
    /// <c>Unverifiable</c> is treated the same as <c>Revoked</c>, matching the
    /// pre-flight check's fail-closed philosophy: a status this handler cannot
    /// confirm is not one it leaves a live credential attached to. For an
    /// existing client it only warns, including when the read is cancelled:
    /// its credential was just rolled and is live, and Identity cannot re-enable it.
    ///
    /// <para>
    /// A second read-after-write check, not a distributed lock: the save has
    /// already committed, so a positive answer here calls
    /// <c>DisableWebhookClientCommand</c>'s own handler directly rather than a
    /// duplicated inline copy of its Keycloak-then-aggregate-then-save steps —
    /// the two paths share one implementation and cannot drift apart. That
    /// call is synchronous inside this request, so it does not inherit the
    /// async disable's own retry-on-redelivery. A failure to disable is
    /// logged and handed to the same retry ladder by publishing
    /// <see cref="WebhookClientRaceDisableFailed"/> into the outbox, which the
    /// caller's single commit flushes (or, once Keycloak has disabled the client,
    /// is committed in place); it is not surfaced, because the create or
    /// rotation itself genuinely succeeded and must not be reported as a failure on
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
    internal async Task<bool> DisableIfRevokedSinceCommitAsync(
        RegisteredClientAggregate aggregate,
        WebhookClientRaceCheck check)
    {
        // Unlinked from the request token on purpose: the save has committed,
        // so a client disconnect must not abandon the compensation. The bound
        // is the only thing allowed to stop it, and an expiry fails closed.
        using CancellationTokenSource bound = new(CompensationBound, timeProvider);
        return await DisableIfRevokedAsync(aggregate, check, bound.Token);
    }

    private async Task<bool> DisableIfRevokedAsync(
        RegisteredClientAggregate aggregate,
        WebhookClientRaceCheck check,
        CancellationToken cancellationToken)
    {
        var (clientId, fab, _, _, _) = check;
        Result<RegisteredClientIdentifier, DisableWebhookClientError> result;
        var disabling = false;
        try
        {
            if (!await ShouldDisableAsync(check, cancellationToken))
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

            LogAlreadyDisabled(check);
            return true;
        }
        catch (OperationCanceledException ex)
        {
            // Deliberately every cancellation, not only the bound's: no request
            // token reaches these awaits, so one that is not the bound's is an
            // internal timeout (HttpClient.Timeout), and a failed disable all the same.
            LogCouldNotDisable(ex, check);
            return await RetryAfterFailedDisableAsync(aggregate, check);
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

            LogCouldNotDisable(ex, check);
            return await RetryAfterFailedDisableAsync(aggregate, check);
        }

        return await AcknowledgeRaceDisableAsync(result, check);
    }

    // Cancellation while reading is caught only for an existing client: its
    // credential is live and Identity cannot re-enable it, so an unconfirmable
    // status is not grounds to disable it. A new client lets it reach the
    // caller's failed-disable path instead.
    private async Task<bool> ShouldDisableAsync(
        WebhookClientRaceCheck check,
        CancellationToken cancellationToken)
    {
        var (clientId, fab, integrationName, disablesOnUnverifiable, _) = check;
        WebhookIntegrationStatus status;
        try
        {
            status = await integrationStatus.GetStatusAsync(fab, integrationName, cancellationToken);
        }
        catch (OperationCanceledException) when (!disablesOnUnverifiable)
        {
            logger.RotationRecheckStatusUnverifiable(integrationName, clientId);
            return false;
        }

        if (!disablesOnUnverifiable && status is WebhookIntegrationStatus.Unverifiable)
        {
            logger.RotationRecheckStatusUnverifiable(integrationName, clientId);
            return false;
        }

        return status is WebhookIntegrationStatus.Revoked or WebhookIntegrationStatus.Unverifiable;
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
        WebhookClientRaceCheck check)
    {
        var (_, fab, integrationName, _, _) = check;

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
        WebhookClientRaceCheck check)
    {
        var (clientId, fab, integrationName, _, logging) = check;

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
                LogAlreadyDisabled(check);
                return true;
            }

            LogCouldNotDisable(
                new InvalidOperationException(
                    $"DisableWebhookClientCommand failed for '{integrationName}': {result.Error.Code}"),
                check);
            await PublishRetryAsync(integrationName, fab);
            return false;
        }

        logging.Disabled(integrationName, clientId);

        return true;
    }

    private static void LogAlreadyDisabled(WebhookClientRaceCheck check)
    {
        var (clientId, _, integrationName, _, logging) = check;
        logging.AlreadyDisabled(integrationName, clientId);
    }

    private static void LogCouldNotDisable(
        Exception exception,
        WebhookClientRaceCheck check)
    {
        var (clientId, _, integrationName, _, logging) = check;
        logging.CouldNotDisable(exception, integrationName, clientId);
    }
}
