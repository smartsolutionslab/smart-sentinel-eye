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
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/cameras")
            .WithTags("Cameras")
            .RequireAuthorization(AuthenticationDefaults.AdminPolicy);

        group.MapPost("/", Register)
            .WithName("RegisterCamera")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", List)
            .WithName("ListCameras")
            .Produces<CameraListPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterCameraRequest request,
        [FromServices] RegisterCameraCommandHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        OperatorIdentifier registeredBy = ResolveOperator(httpContext);

        RegisterCameraCommand command = new(name, url, registeredBy);

        Result<CameraIdentifier, RegisterCameraError> result =
            await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Created(
                $"/cameras/{identifier.Value}",
                identifier.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> List(
        [FromServices] ListCamerasQueryHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? sort = null,
        [FromQuery] string? order = null,
        [FromQuery] int? offset = null,
        [FromQuery] int? limit = null)
    {
        ListCamerasQuery query = new(
            Sort: sort ?? ListCamerasDefaults.DefaultSort,
            Order: order ?? ListCamerasDefaults.DefaultOrder,
            Offset: offset ?? ListCamerasDefaults.DefaultOffset,
            Limit: limit ?? ListCamerasDefaults.DefaultLimit);

        Result<CameraListPageDto, ListCamerasError> result =
            await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
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
            string allClaims = string.Join(", ", httpContext.User.Claims.Select(c => $"{c.Type}={c.Value}"));
            throw new InvalidOperationException(
                $"Authenticated principal is missing the subject claim. Available claims: {allClaims}");
        }

        if (!Guid.TryParse(subject, out Guid subjectId))
        {
            throw new InvalidOperationException($"Subject claim is not a valid Guid: {subject}.");
        }

        return OperatorIdentifier.From(subjectId);
    }
}
