using SmartSentinelEye.Shared.Kernel;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// EventIngestion's binding of the shared decision table (ADR-0114) to its own
/// <see cref="FabIdentifier"/>.
///
/// <para>
/// Shared by both endpoint groups: the operator-driven event write
/// (<c>POST /events/manual</c>) and the webhook integration registry, which
/// became fab-owned in the spec 018 amendment (#1545). One copy, because two
/// would drift and the drift would be a tenancy hole.
/// </para>
/// </summary>
internal static class EventIngestionFabResolution
{
    /// <summary>
    /// The single fab a write applies to (spec 018 FR-006). Inferred when the
    /// caller holds exactly one and named none; refused when they hold several
    /// and named none, because any tie-break would file the write into a plant
    /// they did not choose.
    /// </summary>
    public static async Task<(FabIdentifier? Fab, IResult? Problem)> ResolveWriteFabAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        Result<string, IResult> resolution = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "EVENT_FAB_REQUIRED", cancellationToken);
        if (resolution.IsFailure)
        {
            return (null, resolution.Error);
        }

        try
        {
            return (FabIdentifier.From(resolution.Value), null);
        }
        catch (ArgumentException ex)
        {
            return (null, Results.Problem(
                title: "EVENT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// The fabs a read may span (spec 018 FR-001, FR-003). Omitting a fab spans
    /// every fab the caller holds; naming one narrows to it; naming one they do
    /// not hold is refused.
    ///
    /// <para>
    /// This replaces taking the fab off the query string and trusting it. The
    /// handlers already filtered on a fab — what was missing was any check that
    /// the caller was entitled to the one they named, which is why the context
    /// looked fab-scoped from every angle except this one.
    /// </para>
    ///
    /// <para>
    /// Parsed per entry rather than all-or-nothing. One group under
    /// <c>/fabs/</c> that is not a usable fab name would otherwise fail the
    /// whole read, hiding every event in the fabs the caller legitimately
    /// holds. Mirrors <c>CameraEndpoints</c>, where that was a real defect.
    /// </para>
    /// </summary>
    public static async Task<(IReadOnlyList<FabIdentifier>? Fabs, IResult? Problem)> ResolveReadFabsAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> resolved =
            await FabResolution.ResolveForReadAsync(user, fabId, fabGuard, cancellationToken);

        List<FabIdentifier> fabs = [];
        foreach (string candidate in resolved)
        {
            try
            {
                fabs.Add(FabIdentifier.From(candidate));
            }
            catch (ArgumentException)
            {
                // Skipped, not reported: a caller cannot act on a message about
                // someone else's group configuration, and if nothing is usable
                // the request still fails below.
            }
        }

        if (fabs.Count == 0)
        {
            return (null, Results.Problem(
                title: "EVENT_FAB_REQUIRED",
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return (fabs, null);
    }
}
