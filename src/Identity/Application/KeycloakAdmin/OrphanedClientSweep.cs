using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.KeycloakAdmin;

/// <summary>
/// Disables a Keycloak client this system stamped <c>sse.kind=device</c>,
/// <c>sse.kind=kiosk</c> or <c>sse.kind=webhook</c> that a cancelled
/// registration, enrolment or rotation left behind — created in Keycloak,
/// enabled, and never followed by the <c>RegisteredClient</c> row
/// <c>SaveAsync</c> would have committed (spec 320, #2181; webhook joined
/// by spec 337, #2797 — the identical hole in
/// <c>RotateWebhookClientCommandHandler</c>'s create branch, the same
/// <c>RegisteredClient</c>/<see cref="ClientKind.WebhookIntegration"/> row
/// shape this sweep already queries).
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
    /// §4.5, extended by spec 337/#2797). Anything else is out of scope.
    /// </summary>
    public static readonly IReadOnlySet<string> SweptKinds =
        new HashSet<string>(StringComparer.Ordinal) { "device", "kiosk", "webhook" };

    /// <summary>
    /// One pass: lists stamped clients, subtracts the ones with an active
    /// row, ages the rest past <see cref="GraceWindow"/>, and disables what
    /// is left — unless the mass-disable guard (spec 320 §4.3) refuses.
    /// </summary>
    public async Task<OrphanedClientSweepOutcome> SweepAsync(CancellationToken cancellationToken)
    {
        var stamped = await keycloak.GetStampedClientsAsync(cancellationToken); // S5
        List<StampedClient> eligible =
            [.. stamped.Where(client => SweptKinds.Contains(client.Kind) && client.Enabled)]; // S1, S2
        var active = await clients.GetActiveClientIdsAsync(cancellationToken);

        SweepTally tally = new();
        foreach (var candidate in eligible)
        {
            await ClassifyAsync(candidate, active, tally, cancellationToken);
        }

        // S6, the mass-disable guard. "More than half, and at least two"
        // (spec 320 §4.3) — including the all-orphaned case, which is
        // exactly the restored-or-wrong-database scenario the guard exists
        // to catch. The floor of two is not a substitute for that case; it
        // only rules out a single rare cancellation (#2845).
        if (tally.Orphans.Count >= 2 && tally.Orphans.Count * 2 > eligible.Count)
        {
            logger.OrphanedClientSweepRefused(tally.Orphans.Count, eligible.Count);
            return new OrphanedClientSweepOutcome(eligible.Count, Disabled: 0, Refused: true, tally.Unreachable);
        }

        var disabledCount = await DisableAllAsync(tally, cancellationToken);
        if (disabledCount > 0)
        {
            logger.SweptOrphanedClients(disabledCount, eligible.Count);
        }

        return new OrphanedClientSweepOutcome(eligible.Count, disabledCount, Refused: false, tally.Unreachable);
    }

    /// <summary>
    /// Decides one eligible client: a malformed clientId, an active row
    /// (S3), a service account that cannot be read or does not exist, or one
    /// still inside <see cref="GraceWindow"/> (S4) are each handled and
    /// classified here; everything else still standing is appended to
    /// <see cref="SweepTally.Orphans"/>.
    /// </summary>
    private async Task ClassifyAsync(
        StampedClient candidate,
        IReadOnlySet<ClientId> active,
        SweepTally tally,
        CancellationToken cancellationToken)
    {
        ClientId clientId;
        try
        {
            clientId = ClientId.From(candidate.ClientId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            tally.Unreachable.Add(candidate.ClientId);
            logger.CouldNotSweepOrphanedClient(candidate.ClientId, exception);
            return;
        }

        if (active.Contains(clientId)) // S3
        {
            return;
        }

        var createdAt = await TryReadCreatedAtAsync(candidate.ClientId, tally, cancellationToken);
        if (!createdAt.HasValue)
        {
            return;
        }

        var age = clock.UtcNow - createdAt.Value;
        if (age < GraceWindow) // S4: a registration still in flight
        {
            return;
        }

        tally.Orphans.Add((candidate, age));
    }

    private async Task<Option<DateTimeOffset>> TryReadCreatedAtAsync(
        string clientId,
        SweepTally tally,
        CancellationToken cancellationToken)
    {
        Option<DateTimeOffset> createdAt;
        try
        {
            createdAt = await keycloak.GetServiceAccountCreatedAtAsync(clientId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            tally.Unreachable.Add(clientId);
            logger.CouldNotSweepOrphanedClient(clientId, exception);
            return Option<DateTimeOffset>.None;
        }

        if (!createdAt.HasValue)
        {
            tally.Unreachable.Add(clientId);
            logger.CouldNotSweepOrphanedClient(clientId, exception: null);
        }

        return createdAt;
    }

    private async Task<int> DisableAllAsync(
        SweepTally tally,
        CancellationToken cancellationToken)
    {
        var disabledCount = 0;
        foreach ((var client, var age) in tally.Orphans)
        {
            try
            {
                await keycloak.DisableClientAsync(client.ClientId, cancellationToken); // S7
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                tally.Unreachable.Add(client.ClientId);
                logger.CouldNotSweepOrphanedClient(client.ClientId, exception);
                continue;
            }

            disabledCount++;
            logger.DisabledOrphanedClient(client.ClientId, client.Kind, age);
        }

        return disabledCount;
    }

    /// <summary>
    /// What one pass has found so far: the clients it will disable and the
    /// ones it could not read or disable, in the order they were met.
    /// </summary>
    private sealed class SweepTally
    {
        public List<(StampedClient Client, TimeSpan Age)> Orphans { get; } = [];

        public List<string> Unreachable { get; } = [];
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
