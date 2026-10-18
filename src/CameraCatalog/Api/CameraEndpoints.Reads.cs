using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Api;

/// <summary>Read handlers for <see cref="CameraEndpoints"/>.</summary>
public static partial class CameraEndpoints
{
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> Get(
        [FromRoute] Guid camera,
        [FromServices] GetCameraQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        System.Security.Claims.ClaimsPrincipal user,
        HttpResponse response,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        // Fab first, before anything else is evaluated (FR-007). Nothing about
        // the camera is looked at until the caller's fabs are known, so a
        // refusal cannot be shaped by whether the camera happens to exist.
        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        GetCameraQuery query = new(fabs, CameraIdentifier.From(camera));

        var result =
            await handler.HandleAsync(query, cancellationToken);

        return result.Match<IResult>(
            onSuccess: found =>
            {
                // The version the caller must echo back in If-Match (ADR-0113).
                response.Headers.ETag = ConcurrencyHeaders.ETag(found.Version);

                return Results.Ok(found);
            },
            onFailure: error => error.ToProblem());
    }

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> List(
        [FromServices] ListCamerasQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "",
        [FromQuery] string? sort = null,
        [FromQuery] string? order = null,
        [FromQuery] int? offset = null,
        [FromQuery] int? limit = null,
        [FromQuery] bool includeRetired = false,
        // Spec 055. Optional, and a fragment matching nothing is an empty page
        // rather than a failure: "no camera is called that" is an answer, not an
        // error, and an operator needs to tell it from a request they got wrong.
        [FromQuery] string? name = null)
    {
        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        ListCamerasQuery query = new(
            Fabs: fabs,
            Sort: sort ?? ListCamerasDefaults.DefaultSort,
            Order: order ?? ListCamerasDefaults.DefaultOrder,
            Offset: offset ?? ListCamerasDefaults.DefaultOffset,
            Limit: limit ?? ListCamerasDefaults.DefaultLimit,
            IncludeRetired: includeRetired,
            NameFragment: name);

        var result =
            await handler.HandleAsync(query, cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    /// <summary>
    /// The fabs a read may span. Unlike a write, nothing has to be chosen: a
    /// multi-fab caller listing sees all of theirs (FR-005).
    ///
    /// <para>
    /// Parsed per entry rather than all-or-nothing. One group under
    /// <c>/fabs/</c> that is not a usable fab name would otherwise fail the
    /// whole read, hiding every camera in the fabs the caller legitimately
    /// holds. Mirrors RulesEndpoints, where that was a real defect.
    /// </para>
    /// </summary>
    private static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
        System.Security.Claims.ClaimsPrincipal user,
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
                title: "CAMERA_FAB_REQUIRED",
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return Result<IReadOnlyList<FabIdentifier>, IResult>.Success(fabs);
    }
}
