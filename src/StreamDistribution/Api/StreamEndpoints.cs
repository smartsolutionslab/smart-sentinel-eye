using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
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
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/streams")
            .WithTags("Streams");

        group.MapGet("/{cameraIdentifier:guid}", GetOne)
            .RequireAuthorization(Scope.Sse.Streams.Read)
            .WithName("GetStream")
            .Produces<StreamHealthDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListByCameras)
            .RequireAuthorization(Scope.Sse.Streams.Read)
            .WithName("ListStreams")
            .Produces<IReadOnlyList<StreamHealthDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        // MediaMTX's external auth hook can't carry its own JWT; it forwards
        // the browser's bearer in the POST body. AllowAnonymous lets the
        // route accept the call; the handler validates the forwarded token
        // via IWhepAuthValidator (spec FR-007).
        group.MapPost("/{path}/authorize", AuthorizeWhep)
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
        CancellationToken cancellationToken)
    {
        if (cameraIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "STREAM_INVALID_CAMERA",
                detail: "cameraIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<StreamHealthDto, GetStreamError> result = await handler
            .HandleAsync(new GetStreamQuery(CameraIdentifier.From(cameraIdentifier)), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> ListByCameras(
        [FromQuery] string? cameraIdentifiers,
        [FromServices] ListStreamsQueryHandler handler,
        CancellationToken cancellationToken)
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

        Result<IReadOnlyList<StreamHealthDto>, ListStreamsError> result = await handler
            .HandleAsync(new ListStreamsQuery(parsed), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> AuthorizeWhep(
        string path,
        [FromBody] MediaMtxAuthorizeRequest body,
        [FromServices] AuthorizeWhepCommandHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        MediaMtxPath parsedPath;
        try
        {
            parsedPath = MediaMtxPath.From(path);
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

        Result<MediaMtxPath, AuthorizeWhepError> result = await handler
            .HandleAsync(new AuthorizeWhepCommand(parsedPath, bearer), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: _ => Results.Ok(),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
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
/// We only consume <c>token</c> (the forwarded bearer); other fields are
/// accepted but ignored.
/// </summary>
public sealed record MediaMtxAuthorizeRequest(string? Token);
