using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>
/// Minimal-API endpoint group for LayoutComposition (ADR-0070).
/// Covers spec 003 US1 (create + publish) and US3 (archive) write paths
/// and the US1/US2 read paths (single + list with state filter).
/// </summary>
public static class LayoutEndpoints
{
    public static IEndpointRouteBuilder MapLayoutEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/layouts")
            .WithTags("Layouts");

        group.MapPost("/", CreateDraft)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("CreateLayoutDraft")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{layoutIdentifier:guid}", GetOne)
            .RequireAuthorization(Scope.Sse.Layouts.Read)
            .WithName("GetLayout")
            .Produces<LayoutDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Layouts.Read)
            .WithName("ListLayouts")
            .Produces<ListLayoutsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/publish", Publish)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("PublishRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/archive", Archive)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("ArchiveRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{layoutIdentifier:guid}/draft", BranchDraft)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("BranchDraftRevision")
            .Produces<int>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{layoutIdentifier:guid}/revisions/{revisionNumber:int}", EditDraft)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("EditDraftRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/revert", Revert)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("RevertRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateDraft(
        [FromBody] CreateLayoutRequest body,
        [FromServices] CreateLayoutDraftCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        LayoutName name;
        CameraIdentifier camera;
        OverlayIdentifier? overlay;
        try
        {
            name = LayoutName.From(body.Name);
            camera = CameraIdentifier.From(body.CameraIdentifier);
            overlay = body.OverlayIdentifier is { } overlayId
                ? OverlayIdentifier.From(overlayId)
                : null;
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler
            .HandleAsync(new CreateLayoutDraftCommand(name, camera, op, overlay), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Created($"/layouts/{identifier.Value}", identifier.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> GetOne(
        Guid layoutIdentifier,
        [FromServices] GetLayoutQueryHandler handler,
        CancellationToken cancellationToken)
    {
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<LayoutDto, GetLayoutError> result = await handler
            .HandleAsync(new GetLayoutQuery(LayoutIdentifier.From(layoutIdentifier)), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> List(
        [FromQuery] string? state,
        [FromServices] ListLayoutsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        LayoutRevisionState? filter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            try
            {
                filter = LayoutRevisionState.From(state);
            }
            catch (ArgumentException)
            {
                return Results.Problem(
                    title: "LAYOUT_INVALID_STATE_FILTER",
                    detail: $"'{state}' is not a valid layout state (Draft | Published | Archived).",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        Result<ListLayoutsResult, ListLayoutsError> result = await handler
            .HandleAsync(new ListLayoutsQuery(filter), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: payload => Results.Ok(new ListLayoutsResponse(payload.Chains, payload.Published)),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> Publish(
        Guid layoutIdentifier,
        int revisionNumber,
        [FromServices] PublishRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        LayoutRevisionNumber number;
        try
        {
            number = LayoutRevisionNumber.From(revisionNumber);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler
            .HandleAsync(
                new PublishRevisionCommand(LayoutIdentifier.From(layoutIdentifier), number, op),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: published => Results.Ok(published.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> Archive(
        Guid layoutIdentifier,
        int revisionNumber,
        [FromServices] ArchiveRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        LayoutRevisionNumber number;
        try
        {
            number = LayoutRevisionNumber.From(revisionNumber);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<LayoutRevisionNumber, ArchiveRevisionError> result = await handler
            .HandleAsync(
                new ArchiveRevisionCommand(LayoutIdentifier.From(layoutIdentifier), number, op),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: archived => Results.Ok(archived.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> BranchDraft(
        Guid layoutIdentifier,
        [FromServices] BranchDraftRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler
            .HandleAsync(
                new BranchDraftRevisionCommand(LayoutIdentifier.From(layoutIdentifier), op),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: branched => Results.Created(
                $"/layouts/{layoutIdentifier}/revisions/{branched.Value}", branched.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> EditDraft(
        Guid layoutIdentifier,
        int revisionNumber,
        [FromBody] EditDraftRequest body,
        [FromServices] EditDraftRevisionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        LayoutRevisionNumber number;
        CameraIdentifier camera;
        OverlayChange overlayChange;
        try
        {
            number = LayoutRevisionNumber.From(revisionNumber);
            camera = CameraIdentifier.From(body.CameraIdentifier);
            overlayChange = TranslateOverlayChange(body.Overlay);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler
            .HandleAsync(
                new EditDraftRevisionCommand(LayoutIdentifier.From(layoutIdentifier), number, camera, overlayChange),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: edited => Results.Ok(edited.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> Revert(
        Guid layoutIdentifier,
        int revisionNumber,
        [FromServices] RevertRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        LayoutRevisionNumber number;
        try
        {
            number = LayoutRevisionNumber.From(revisionNumber);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<LayoutRevisionNumber, RevertRevisionError> result = await handler
            .HandleAsync(
                new RevertRevisionCommand(LayoutIdentifier.From(layoutIdentifier), number, op),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: reverted => Results.Ok(reverted.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static OverlayChange TranslateOverlayChange(OverlayBindingUpdate? update)
    {
        if (update is null) return OverlayChange.None;
        if (update.Identifier is { } overlayId) return OverlayChange.Set(OverlayIdentifier.From(overlayId));
        return OverlayChange.Clear();
    }

    private static OperatorIdentifier OperatorFromClaims(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        string? raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out Guid value) && value != Guid.Empty
            ? OperatorIdentifier.From(value)
            : OperatorIdentifier.From(Guid.CreateVersion7());
    }
}

/// <summary>
/// Envelope shape returned by <c>GET /layouts</c>. Either
/// <see cref="Chains"/> (admin view) or <see cref="Published"/>
/// (kiosk picker) is populated depending on the state filter; the
/// other is empty.
/// </summary>
public sealed record ListLayoutsResponse(
    IReadOnlyList<LayoutDto> Chains,
    IReadOnlyList<PublishedLayoutDto> Published);
