using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.KeycloakAdmin;

/// <summary>
/// Disables a Keycloak client this system stamped <c>sse.kind=device</c> or
/// <c>sse.kind=kiosk</c> that a cancelled registration or enrolment left
/// behind — created in Keycloak, enabled, and never followed by the
/// <c>RegisteredClient</c> row <c>SaveAsync</c> would have committed
/// (spec 320, #2181).
///
/// <para>
/// <b>Mirrors <see cref="KioskPrivilegeSweep"/></b> (ADR-0134 Decision 1): a
/// pass in Application, a hosted wrapper in Infrastructure, per-client
/// failure isolation, and silence when nothing was repaired.
/// </para>
///
/// <para>
/// <b>Disables, does not delete</b> (spec 320 §4.1) — reversible, and
/// re-registration in the same fab is already unblocked by #2728's replace
/// path. <b>Reads Keycloak before Postgres</b> (§4.2, S5) and skips a
/// candidate younger than <see cref="GraceWindow"/>, the two halves of the
/// in-flight-registration guard. <b>Refuses outright</b> when the orphan
/// count would disable most of the fleet (§4.3) — a restored or wrong
/// database, not a rare cancellation.
/// </para>
/// </summary>
public sealed class OrphanedClientSweep(
    IKeycloakAdminClient keycloak,
    IRegisteredClientRepository clients,
    IClock clock,
    ILogger<OrphanedClientSweep> logger)
{
    /// <summary>
    /// How old a stamped client's service account must be before a missing
    /// row makes it an orphan rather than a registration still in flight
    /// (spec 320 §3). Not configuration (ADR-0036).
    /// </summary>
    public static readonly TimeSpan GraceWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The only <c>sse.kind</c> values this sweep ever acts on (spec 320
    /// §4.5). <c>webhook</c> and anything else is out of scope.
    /// </summary>
    public static readonly IReadOnlySet<string> SweptKinds =
        new HashSet<string>(StringComparer.Ordinal) { "device", "kiosk" };

    /// <summary>
    /// One pass: lists stamped clients, subtracts the ones with an active
    /// row, ages the rest past <see cref="GraceWindow"/>, and disables what
    /// is left — unless the mass-disable guard (spec 320 §4.3) refuses.
    /// </summary>
    public async Task<OrphanedClientSweepOutcome> SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StampedClient> stamped = await keycloak.GetStampedClientsAsync(cancellationToken); // S5
        List<StampedClient> eligible =
            [.. stamped.Where(client => SweptKinds.Contains(client.Kind) && client.Enabled)]; // S1, S2
        IReadOnlySet<ClientId> active = await clients.GetActiveClientIdsAsync(cancellationToken);

        List<string> unreachable = [];
        List<(StampedClient Client, TimeSpan Age)> orphans = [];
        foreach (StampedClient candidate in eligible)
        {
            await ClassifyAsync(candidate, active, orphans, unreachable, cancellationToken);
        }

        // S6, the mass-disable guard. A healthy baseline must exist to ratio
        // against — when every eligible client is orphaned there is nothing
        // to compare against a restored or wrong database, and the candidate
        // floor is what protects that case instead (spec 320 §4.3).
        if (orphans.Count >= 2 && orphans.Count < eligible.Count && orphans.Count * 2 > eligible.Count)
        {
            logger.OrphanedClientSweepRefused(orphans.Count, eligible.Count);
            return new OrphanedClientSweepOutcome(eligible.Count, Disabled: 0, Refused: true, unreachable);
        }

        int disabledCount = await DisableAllAsync(orphans, unreachable, cancellationToken);
        if (disabledCount > 0)
        {
            logger.SweptOrphanedClients(disabledCount, eligible.Count);
        }

        return new OrphanedClientSweepOutcome(eligible.Count, disabledCount, Refused: false, unreachable);
    }

    /// <summary>
    /// Decides one eligible client: a malformed clientId, an active row
    /// (S3), a service account that cannot be read or does not exist, or one
    /// still inside <see cref="GraceWindow"/> (S4) are each handled and
    /// classified here; everything else still standing is appended to
    /// <paramref name="orphans"/>.
    /// </summary>
    private async Task ClassifyAsync(
        StampedClient candidate,
        IReadOnlySet<ClientId> active,
        List<(StampedClient Client, TimeSpan Age)> orphans,
        List<string> unreachable,
        CancellationToken cancellationToken)
    {
        ClientId clientId;
        try
        {
            clientId = ClientId.From(candidate.ClientId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            unreachable.Add(candidate.ClientId);
            logger.CouldNotSweepOrphanedClient(candidate.ClientId, exception);
            return;
        }

        if (active.Contains(clientId)) // S3
        {
            return;
        }

        Option<DateTimeOffset> createdAt;
        try
        {
            createdAt = await keycloak.GetServiceAccountCreatedAtAsync(candidate.ClientId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            unreachable.Add(candidate.ClientId);
            logger.CouldNotSweepOrphanedClient(candidate.ClientId, exception);
            return;
        }

        if (!createdAt.HasValue)
        {
            unreachable.Add(candidate.ClientId);
            logger.CouldNotSweepOrphanedClient(candidate.ClientId, exception: null);
            return;
        }

        TimeSpan age = clock.UtcNow - createdAt.Value;
        if (age < GraceWindow) // S4: a registration still in flight
        {
            return;
        }

        orphans.Add((candidate, age));
    }

    private async Task<int> DisableAllAsync(
        List<(StampedClient Client, TimeSpan Age)> orphans,
        List<string> unreachable,
        CancellationToken cancellationToken)
    {
        int disabledCount = 0;
        foreach ((StampedClient client, TimeSpan age) in orphans)
        {
            try
            {
                await keycloak.DisableClientAsync(client.ClientId, cancellationToken); // S7
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                unreachable.Add(client.ClientId);
                logger.CouldNotSweepOrphanedClient(client.ClientId, exception);
                continue;
            }

            disabledCount++;
            logger.DisabledOrphanedClient(client.ClientId, client.Kind, age);
        }

        return disabledCount;
    }
}

/// <summary>
/// What one pass reached and what it did (spec 320 plan §4). <paramref name="Examined"/>
/// is the enabled stamped-client population the pass was allowed to act on;
/// <paramref name="Disabled"/> is how many it actually disabled.
/// <paramref name="Refused"/> is <c>true</c> when the mass-disable guard
/// stopped the pass from disabling anything. <paramref name="Unreachable"/>
/// names every client the pass could not read or could not disable.
/// </summary>
public sealed record OrphanedClientSweepOutcome(
    int Examined, int Disabled, bool Refused, IReadOnlyList<string> Unreachable);
