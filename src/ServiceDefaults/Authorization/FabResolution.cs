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
/// That mattered more than usual while the realm had a single fab group and
/// no multi-fab user: the multi-fab branch was unreachable end to end, so
/// these unit tests were its only coverage. The realm now seeds a second fab
/// and a multi-fab operator, and <c>RuleFabResolutionIntegrationTests</c>
/// drives the same rows over HTTP.
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
    public static async Task<Result<string, IResult>> ResolveForWriteAsync(
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

            return Result<string, IResult>.Success(fabId);
        }

        IReadOnlyList<string> assigned = FabClaims.AssignedFabs(user);

        if (assigned.Count == 1)
        {
            return Result<string, IResult>.Success(assigned[0]);
        }

        if (assigned.Count == 0)
        {
            // Refused rather than answered emptily: an operator assigned to no
            // fab is a misconfiguration worth surfacing. Thrown directly rather
            // than routed through the guard with a placeholder fab — there is
            // no fab to check, and pretending otherwise put the placeholder in
            // the operator's error message.
            throw FabAuthorizationException.ForNoFabMembership();
        }

        return Result<string, IResult>.Failure(Results.Problem(
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
            // Same refusal as the write path, and for the same reason: an
            // empty answer would read as "there is nothing here" when what is
            // true is "you are not configured to see anything".
            throw FabAuthorizationException.ForNoFabMembership();
        }

        return assigned;
    }
}
