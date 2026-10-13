using System.Security.Claims;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>
/// LayoutComposition's fab resolution, shared by <see cref="LayoutEndpoints"/> and
/// <see cref="WallEndpoints"/>.
/// </summary>
internal static class LayoutCompositionFabResolution
{
    /// <summary>
    /// LayoutComposition's binding of the shared decision table (ADR-0114) to
    /// its own <see cref="FabIdentifier"/>. The table itself lives in
    /// <see cref="FabResolution"/>; this feature adds no resolution mechanism,
    /// it applies the existing one (spec 017).
    ///
    /// <para>
    /// Only <c>POST /layouts</c> uses this. The other five writes address an
    /// existing layout, which already has a fab — letting the caller name one
    /// there would allow the two to disagree.
    /// </para>
    /// </summary>
    internal static async Task<Result<FabIdentifier, IResult>> ResolveWriteFabAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        var resolution = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "LAYOUT_FAB_REQUIRED", cancellationToken);
        if (resolution.IsFailure)
        {
            return Result<FabIdentifier, IResult>.Failure(resolution.Error);
        }

        try
        {
            return Result<FabIdentifier, IResult>.Success(FabIdentifier.From(resolution.Value));
        }
        catch (ArgumentException ex)
        {
            return Result<FabIdentifier, IResult>.Failure(Results.Problem(
                title: "LAYOUT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Every fab the caller holds, with no narrowing — what the five
    /// layout-addressing writes need.
    ///
    /// <para>
    /// They deliberately accept no <c>?fabId=</c>: the layout already has a
    /// fab, so letting the caller name one would allow the two to disagree.
    /// The fabs are used to scope the lookup, so a layout outside them is
    /// reported exactly as one that never existed (FR-006).
    /// </para>
    /// </summary>
    internal static Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveCallerFabsAsync(
        ClaimsPrincipal user,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken) =>
        ResolveReadFabsAsync(user, fabId: string.Empty, fabGuard, cancellationToken);

    /// <summary>
    /// The fabs a read may span. Unlike a write, nothing has to be chosen: a
    /// multi-fab caller listing sees all of theirs (FR-005).
    ///
    /// <para>
    /// Parsed per entry rather than all-or-nothing. One group under
    /// <c>/fabs/</c> that is not a usable fab name would otherwise fail the
    /// whole read, hiding every layout in the fabs the caller legitimately
    /// holds. Mirrors <c>CameraEndpoints</c>, where that was a real defect.
    /// </para>
    /// </summary>
    internal static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
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
                title: "LAYOUT_FAB_REQUIRED",
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return Result<IReadOnlyList<FabIdentifier>, IResult>.Success(fabs);
    }
}
