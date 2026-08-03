using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Works out which fab a request concerns, from an explicit <c>fabId</c> or
/// from the caller's group membership (ADR-0114).
///
/// <para>
/// Extracted from the endpoint so the decision table can be tested directly.
/// The multi-fab branch cannot be reached in the current deployment — the
/// realm has one fab group and no multi-fab user — so it would otherwise
/// ship with no coverage at all, which is exactly the branch most likely to
/// regress unnoticed.
/// </para>
///
/// <para>
/// Living here makes the mechanism available to any context; it does not
/// make it adopted. ADR-0114 scopes fab *inference* to Automation's rule
/// endpoints, and using <see cref="ResolveForWriteAsync"/> elsewhere is a new
/// decision rather than an application of that one.
/// </para>
///
/// <para>
/// Returns fab names as strings rather than a value object: each context owns
/// its own <c>FabIdentifier</c> (ADR-0044) and ServiceDefaults cannot
/// reference any of them. The caller parses.
/// </para>
/// </summary>
public static class FabResolution
{
    /// <summary>
    /// The single fab a write applies to.
    ///
    /// <para>
    /// Inferred when the caller belongs to exactly one and named none.
    /// Refused when they belong to several and named none — any tie-break
    /// would silently place the write in a fab they did not choose.
    /// </para>
    ///
    /// <para>
    /// Throws <see cref="FabAuthorizationException"/> (403 globally) when the
    /// caller names a fab they do not hold, or holds none at all.
    /// </para>
    /// </summary>
    public static async Task<(string Fab, IResult Problem)> ResolveForWriteAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        string ambiguousErrorCode,
        CancellationToken cancellationToken)
    {
        Ensure.That(user).IsNotNull();
        Ensure.That(fabGuard).IsNotNull();
        Ensure.That(ambiguousErrorCode).IsNotNullOrWhiteSpace();

        if (!string.IsNullOrWhiteSpace(fabId))
        {
            await fabGuard.EnsureAccessAsync(user, fabId, cancellationToken);

            return (fabId, null);
        }

        IReadOnlyList<string> assigned = FabClaims.AssignedFabs(user);

        if (assigned.Count == 1)
        {
            return (assigned[0], null);
        }

        if (assigned.Count == 0)
        {
            // Refused rather than answered emptily: an operator assigned to no
            // fab is a misconfiguration worth surfacing. The literal is never
            // a real fab, so the guard always rejects it.
            await fabGuard.EnsureAccessAsync(user, "none", cancellationToken);
        }

        return (null, Results.Problem(
            title: ambiguousErrorCode,
            detail: "You are assigned to more than one fab; name the one this applies to with ?fabId=.",
            statusCode: StatusCodes.Status400BadRequest));
    }

    /// <summary>
    /// The fabs a read may span. Unlike a write, nothing has to be chosen: a
    /// multi-fab caller listing sees all of theirs.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ResolveForReadAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        Ensure.That(user).IsNotNull();
        Ensure.That(fabGuard).IsNotNull();

        if (!string.IsNullOrWhiteSpace(fabId))
        {
            await fabGuard.EnsureAccessAsync(user, fabId, cancellationToken);

            return [fabId];
        }

        IReadOnlyList<string> assigned = FabClaims.AssignedFabs(user);
        if (assigned.Count == 0)
        {
            await fabGuard.EnsureAccessAsync(user, "none", cancellationToken);
        }

        return assigned;
    }
}
