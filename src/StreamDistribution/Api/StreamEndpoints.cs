using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Api;

/// <summary>
/// Minimal-API endpoint group for Stream Distribution (ADR-0070). Two read
/// routes for the management UI (single + batch) and one MediaMTX-callback
/// route for WHEP bearer-token validation.
/// </summary>
public static class StreamEndpoints
{
    public static IEndpointRouteBuilder MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder group = app.MapGroup("/streams")
            .WithTags("Streams");

        // 403 became reachable with spec 016 (T017): a caller may name a fab
        // they do not hold, or hold none at all. Declaring it keeps the
        // generated OpenAPI from claiming a status that can happen cannot;
        // spec 013 shipped this wrong on one endpoint and it took a review to
        // catch.
        group.MapGet("/{cameraIdentifier:guid}", GetOne)
            .RequireAuthorization(Scope.Sse.Streams.Read)
            .WithName("GetStream")
            .WithSummary(
                "Get one camera's stream, within your fabs. A stream in a fab you do not hold "
                + "is reported exactly as a camera with no stream (spec 016 FR-006).")
            .Produces<StreamHealthDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListByCameras)
            .RequireAuthorization(Scope.Sse.Streams.Read)
            .WithName("ListStreams")
            .WithSummary(
                "Batch-read stream health within your fabs. Omit fabId to span all of them; "
                + "name one to narrow.")
            .Produces<IReadOnlyList<StreamHealthDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // MediaMTX's external auth hook POSTs to a single fixed address with the
        // stream path + the client's bearer in the JSON body — it can't template
        // the path into the URL, so the route is fixed and the path is read from
        // the body. AllowAnonymous lets the route accept the call; the handler
        // validates the forwarded token via IWhepAuthValidator (spec FR-007).
        group.MapPost("/authorize", AuthorizeWhep)
            .AllowAnonymous()
            .WithName("AuthorizeWhep")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetOne(
        Guid cameraIdentifier,
        [FromServices] GetStreamQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        if (cameraIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "STREAM_INVALID_CAMERA",
                detail: "cameraIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await ResolveReadFabsAsync(httpContext.User, fabId, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

        GetStreamQuery query = new(fabs, CameraIdentifier.From(cameraIdentifier));
        Result<StreamHealthDto, GetStreamError> result = await handler.HandleAsync(query, cancellationToken);

        return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> ListByCameras(
        [FromQuery] string cameraIdentifiers,
        [FromServices] ListStreamsQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        IReadOnlyList<CameraIdentifier> parsed;
        try
        {
            parsed = ParseCameraIdentifiers(cameraIdentifiers);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "STREAM_INVALID_CAMERA_LIST",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await ResolveReadFabsAsync(httpContext.User, fabId, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

        Result<IReadOnlyList<StreamHealthDto>, ListStreamsError> result =
            await handler.HandleAsync(new ListStreamsQuery(fabs, parsed), cancellationToken);

        return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
    }

    /// <summary>
    /// The fabs a read may span. StreamDistribution's binding of the shared
    /// decision table (ADR-0114) to its own <see cref="FabIdentifier"/>; the
    /// table itself is <see cref="FabResolution"/>, used unchanged.
    ///
    /// <para>
    /// Parsed per entry rather than all-or-nothing, mirroring
    /// <c>CameraEndpoints</c>: one group under <c>/fabs/</c> that is not a
    /// usable fab name would otherwise hide every stream in the fabs the caller
    /// legitimately holds.
    /// </para>
    /// </summary>
    private static async Task<(IReadOnlyList<FabIdentifier>? Fabs, IResult? Problem)> ResolveReadFabsAsync(
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
                title: "STREAM_FAB_UNUSABLE",
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return (fabs, null);
    }

    private static async Task<IResult> AuthorizeWhep(
        [FromBody] MediaMtxAuthorizeRequest body,
        [FromServices] AuthorizeWhepCommandHandler handler,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        MediaMtxPath parsedPath;
        try
        {
            parsedPath = MediaMtxPath.From(body.Path ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return Results.Problem(
                title: "WHEP_INVALID_PATH",
                detail: "Path does not match the cam-{guid} pattern.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        string bearer = body.Token ?? string.Empty;
        if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            bearer = bearer["Bearer ".Length..].Trim();
        }

        Result<MediaMtxPath, AuthorizeWhepError> result = await handler.HandleAsync(new AuthorizeWhepCommand(parsedPath, bearer), cancellationToken);

        return result.Match<IResult>(onSuccess: _ => Results.Ok(), onFailure: error => error.ToProblem());
    }

    private static IReadOnlyList<CameraIdentifier> ParseCameraIdentifiers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<CameraIdentifier>();
        }

        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<CameraIdentifier> result = new(parts.Length);
        foreach (string part in parts)
        {
            if (!Guid.TryParse(part, out Guid value))
            {
                throw new ArgumentException($"'{part}' is not a valid Guid.");
            }
            result.Add(CameraIdentifier.From(value));
        }
        return result;
    }
}

/// <summary>
/// MediaMTX's external-auth POST body. Fields match MediaMTX v1.x
/// (<c>user</c>, <c>password</c>, <c>token</c>, <c>action</c>, <c>path</c>).
/// We consume <c>path</c> (the stream being opened) and <c>token</c> (the
/// forwarded bearer); other fields are accepted but ignored.
/// </summary>
public sealed record MediaMtxAuthorizeRequest(string? Token, string? Path);
