using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Api;

/// <summary>
/// Minimal-API endpoint group for the Camera Catalog (ADR-0070). All routes
/// require the admin policy per spec 001-register-camera FR-010.
/// </summary>
public static class CameraEndpoints
{
    public static IEndpointRouteBuilder MapCameraCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder writes = app.MapGroup("/cameras")
            .WithTags("Cameras")
            .RequireAuthorization(Scope.Sse.Cameras.Write);

        // 403 became reachable when fab resolution landed (spec 015 T024): the
        // caller may name a fab they do not hold, or hold none at all. 400 was
        // already declared but now covers a second cause — a multi-fab operator
        // omitting fabId. Declaring them keeps the generated OpenAPI from
        // claiming a status that can happen cannot; spec 013 shipped this wrong
        // on one endpoint and it took a review to catch.
        writes.MapPost("/", Register)
            .WithName("RegisterCamera")
            .WithSummary(
                "Register a camera in the resolved fab. Omit fabId when you belong to exactly one fab; "
                + "name it when you belong to several (ADR-0114). Required scope: sse.cameras.write")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        RouteGroupBuilder reads = app.MapGroup("/cameras")
            .WithTags("Cameras")
            .RequireAuthorization(Scope.Sse.Cameras.Read);

        reads.MapGet("/", List)
            .WithName("ListCameras")
            .WithSummary(
                "List cameras in your fabs. Omit fabId to span all of them; name one to narrow. "
                + "A read does not have to choose (spec 015 FR-005).")
            .Produces<CameraListPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterCameraRequest request,
        [FromServices] RegisterCameraCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(request).IsNotNull();

        CameraName name;
        RtspUrl url;
        try
        {
            (name, url) = request;
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "CAMERA_INVALID_REQUEST",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        (FabIdentifier? fab, IResult? fabProblem) =
            await ResolveWriteFabAsync(httpContext.User, fabId, fabGuard, cancellationToken);
        if (fab is null)
        {
            return fabProblem!;
        }

        OperatorIdentifier registeredBy = ResolveOperator(httpContext);

        RegisterCameraCommand command = new(fab, name, url, registeredBy);

        Result<CameraIdentifier, RegisterCameraError> result =
            await handler.HandleAsync(command, cancellationToken);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Created(
                $"/cameras/{identifier.Value}",
                identifier.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> List(
        [FromServices] ListCamerasQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "",
        [FromQuery] string? sort = null,
        [FromQuery] string? order = null,
        [FromQuery] int? offset = null,
        [FromQuery] int? limit = null)
    {
        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

        ListCamerasQuery query = new(
            Fabs: fabs,
            Sort: sort ?? ListCamerasDefaults.DefaultSort,
            Order: order ?? ListCamerasDefaults.DefaultOrder,
            Offset: offset ?? ListCamerasDefaults.DefaultOffset,
            Limit: limit ?? ListCamerasDefaults.DefaultLimit);

        Result<CameraListPageDto, ListCamerasError> result =
            await handler.HandleAsync(query, cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static OperatorIdentifier ResolveOperator(HttpContext httpContext)
    {
        // The JWT 'sub' claim ends up under different names depending on whether
        // System.IdentityModel.Tokens.Jwt remapped it (MapInboundClaims=true ->
        // NameIdentifier URI; false -> raw "sub"). Try the common variants and,
        // as a last resort, the first Guid-valued claim on the principal.
        string? subject =
            httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("nameid")?.Value;

        if (subject is null)
        {
            string allClaims = string.Join(", ", httpContext.User.Claims.Select(claim => $"{claim.Type}={claim.Value}"));
            throw new InvalidOperationException(
                $"Authenticated principal is missing the subject claim. Available claims: {allClaims}");
        }

        if (!Guid.TryParse(subject, out Guid subjectId))
        {
            throw new InvalidOperationException($"Subject claim is not a valid Guid: {subject}.");
        }

        return OperatorIdentifier.From(subjectId);
    }

    /// <summary>
    /// CameraCatalog's binding of the shared decision table (ADR-0114) to its
    /// own <see cref="FabIdentifier"/>. The table itself lives in
    /// <see cref="FabResolution"/>; this feature adds no resolution mechanism,
    /// it applies the existing one (spec 015).
    /// </summary>
    private static async Task<(FabIdentifier? Fab, IResult? Problem)> ResolveWriteFabAsync(
        System.Security.Claims.ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        (string resolved, IResult problem) = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "CAMERA_FAB_REQUIRED", cancellationToken);
        if (problem is not null)
        {
            return (null, problem);
        }

        try
        {
            return (FabIdentifier.From(resolved), null);
        }
        catch (ArgumentException ex)
        {
            return (null, Results.Problem(
                title: "CAMERA_INVALID_REQUEST", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
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
    private static async Task<(IReadOnlyList<FabIdentifier>? Fabs, IResult? Problem)> ResolveReadFabsAsync(
        System.Security.Claims.ClaimsPrincipal user,
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
                title: "CAMERA_FAB_REQUIRED",
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return (fabs, null);
    }
}
