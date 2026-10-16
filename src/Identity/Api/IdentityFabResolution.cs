using System.Security.Claims;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Api;

/// <summary>
/// Identity's binding of the shared decision table (ADR-0114) to its own
/// <see cref="FabIdentifier"/>.
///
/// <para>
/// Shared by all three <c>List</c> endpoints (<c>KiosksEndpoints</c>,
/// <c>DevicesEndpoints</c>, <c>WebhookRotationEndpoints</c>) rather than
/// copied into each. Three private copies would drift, and the drift would
/// be a tenancy hole: a fix applied to one endpoint's copy but not
/// another's is exactly how a fab boundary quietly reopens (spec 183).
/// </para>
/// </summary>
internal static class IdentityFabResolution
{
    /// <summary>
    /// The fabs a read may span (spec 183 AS-1/AS-4). Omitting
    /// <see cref="FabReadRequest.Fab"/> spans every fab the caller holds; naming one
    /// narrows to it; naming one they do not hold is refused.
    ///
    /// <para>
    /// Parsed per entry rather than all-or-nothing. One group under
    /// <c>/fabs/</c> that is not a usable fab name would otherwise fail the
    /// whole read, hiding every client in the fabs the caller legitimately
    /// holds. Mirrors <c>CameraEndpoints</c>, where that was a real defect;
    /// <c>EventIngestionFabResolution</c> carries the same fix, copied
    /// forward rather than discovered independently there.
    /// </para>
    /// </summary>
    public static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
        FabReadRequest read,
        IFabAuthorizationGuard fabGuard,
        string unusableFabErrorCode,
        CancellationToken cancellationToken)
    {
        var (user, fabId) = read;

        var resolved =
            await FabResolution.ResolveForReadAsync(user, fabId, fabGuard, cancellationToken);

        List<FabIdentifier> fabs = [];
        foreach (var candidate in resolved)
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
            return Result<IReadOnlyList<FabIdentifier>, IResult>.Failure(Results.Problem(
                title: unusableFabErrorCode,
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return Result<IReadOnlyList<FabIdentifier>, IResult>.Success(fabs);
    }
}

/// <summary>
/// The caller and the fab they asked to read, carried together so that
/// <c>ResolveReadFabsAsync</c> stays within ADR-0084's four parameters.
/// </summary>
/// <param name="User">The authenticated caller whose fab groups bound the read.</param>
/// <param name="Fab">The fab named on the request; empty spans every fab the caller holds.</param>
internal sealed record FabReadRequest(ClaimsPrincipal User, string Fab);
