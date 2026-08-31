using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.Identity.Application.KeycloakAdmin;

/// <summary>
/// Takes the realm's inherited privileges off every kiosk account this system
/// enrolled (spec 052 US1).
///
/// <para>
/// <b>Why a sweep and not only the enrolment path.</b> Every kiosk enrolled
/// before this existed is still holding a privilege that mints credentials
/// which never expire. Fixing new enrolments alone would leave the claim — only
/// a wall display may hold it — true of the future and false of the present.
/// </para>
///
/// <para>
/// <b>Safe to run on every start.</b> The removal is idempotent, so this doubles
/// as reconciliation: an account that somehow regains the privilege loses it at
/// the next boot.
/// </para>
///
/// <para>
/// <b>Bounded to accounts enrolment created</b>, because the removal takes away
/// every directly-assigned realm privilege. Applied to a person's account that
/// would strip them of everything, and it would not fail while doing it.
/// </para>
/// </summary>
public sealed class KioskPrivilegeSweep(
    IKeycloakAdminClient keycloak,
    ILogger<KioskPrivilegeSweep> logger)
{
    /// <summary>
    /// Strips every enrolled kiosk, and reports how many were reached.
    ///
    /// <para>
    /// One kiosk failing does not stop the rest: the others are independent, and
    /// leaving them holding the privilege because a different account could not
    /// be read would be the wrong trade. Each failure is logged, and the next
    /// start tries again.
    /// </para>
    /// </summary>
    public async Task<KioskSweepOutcome> SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> kiosks = await keycloak.GetEnrolledKioskClientIdsAsync(cancellationToken);

        List<string> unreachable = [];
        foreach (string clientId in kiosks)
        {
            try
            {
                await keycloak.StripInheritedRealmRolesAsync(clientId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                unreachable.Add(clientId);
                logger.CouldNotSweepKiosk(clientId, exception);
            }
        }

        logger.SweptKioskPrivileges(kiosks.Count - unreachable.Count, kiosks.Count);

        return new KioskSweepOutcome(kiosks.Count, unreachable);
    }
}

/// <summary>
/// What a sweep reached. <paramref name="Unreachable"/> is named rather than
/// counted so a caller can say which kiosk still holds the privilege.
/// </summary>
public sealed record KioskSweepOutcome(int KioskCount, IReadOnlyList<string> Unreachable)
{
    public int StrippedCount => KioskCount - Unreachable.Count;
}
