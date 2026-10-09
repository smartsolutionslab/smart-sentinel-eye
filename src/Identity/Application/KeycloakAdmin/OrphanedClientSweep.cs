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
    public Task<OrphanedClientSweepOutcome> SweepAsync(CancellationToken cancellationToken)
    {
        // T001 (spec 320, #2181): declaration only. The four collaborators
        // below are read here so the primary constructor compiles clean of
        // CS9113/S2325 ahead of T011's real implementation (plan §4).
        _ = keycloak;
        _ = clients;
        _ = clock;
        _ = logger;
        throw new NotImplementedException();
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
