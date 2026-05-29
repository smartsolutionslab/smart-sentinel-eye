using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.OverlayDesigner.Api.Requests;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Application.Queries;
using SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Api;

/// <summary>
/// Minimal-API endpoint group for OverlayDesigner (ADR-0070). Spec 004
/// US1 (create + publish), US1's archive write path, and the single +
/// list read paths. Branch / Edit / Revert endpoints land in PR F.
/// </summary>
public static class OverlayEndpoints
{
    public static IEndpointRouteBuilder MapOverlayEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/overlays")
            .WithTags("Overlays");

        group.MapPost("/", CreateDraft)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("CreateOverlayDraft")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{overlayIdentifier:guid}", GetOne)
            .RequireAuthorization(Scope.Sse.Overlays.Read)
            .WithName("GetOverlay")
            .Produces<OverlayDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Overlays.Read)
            .WithName("ListOverlays")
            .Produces<ListOverlaysResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/publish", Publish)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("PublishOverlayRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/archive", Archive)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("ArchiveOverlayRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{overlayIdentifier:guid}/draft", BranchDraft)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("BranchDraftOverlayRevision")
            .Produces<int>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{overlayIdentifier:guid}/revisions/{revisionNumber:int}", EditDraft)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("EditDraftOverlayRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/revert", Revert)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("RevertOverlayRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateDraft(
        [FromBody] CreateOverlayRequest body,
        [FromServices] CreateOverlayDraftCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(body.Label);

        OverlayName name;
        Label label;
        try
        {
            name = OverlayName.From(body.Name);
            label = Label.From(
                body.Label.Text,
                body.Label.NormalizedX,
                body.Label.NormalizedY,
                body.Label.NormalizedWidth,
                body.Label.NormalizedHeight,
                body.Label.FontSizePx);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<OverlayIdentifier, CreateOverlayDraftError> result = await handler
            .HandleAsync(new CreateOverlayDraftCommand(name, label, op), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Created($"/overlays/{identifier}", identifier.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> GetOne(
        Guid overlayIdentifier,
        [FromServices] GetOverlayQueryHandler handler,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<OverlayDto, GetOverlayError> result = await handler
            .HandleAsync(new GetOverlayQuery(OverlayIdentifier.From(overlayIdentifier)), cancellationToken)
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
        [FromServices] ListOverlaysQueryHandler handler,
        CancellationToken cancellationToken)
    {
        OverlayRevisionState? filter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            try
            {
                filter = OverlayRevisionState.From(state);
            }
            catch (ArgumentException)
            {
                return Results.Problem(
                    title: "OVERLAY_INVALID_STATE_FILTER",
                    detail: $"'{state}' is not a valid overlay state (Draft | Published | Archived).",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        Result<ListOverlaysResult, ListOverlaysError> result = await handler
            .HandleAsync(new ListOverlaysQuery(filter), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: payload => Results.Ok(new ListOverlaysResponse(payload.Chains, payload.Published)),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> Publish(
        Guid overlayIdentifier,
        int revisionNumber,
        [FromServices] PublishRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        OverlayRevisionNumber number;
        try
        {
            number = OverlayRevisionNumber.From(revisionNumber);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<OverlayRevisionNumber, PublishRevisionError> result = await handler
            .HandleAsync(
                new PublishRevisionCommand(OverlayIdentifier.From(overlayIdentifier), number, op),
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
        Guid overlayIdentifier,
        int revisionNumber,
        [FromServices] ArchiveRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        OverlayRevisionNumber number;
        try
        {
            number = OverlayRevisionNumber.From(revisionNumber);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<OverlayRevisionNumber, ArchiveRevisionError> result = await handler
            .HandleAsync(
                new ArchiveRevisionCommand(OverlayIdentifier.From(overlayIdentifier), number, op),
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
        Guid overlayIdentifier,
        [FromServices] BranchDraftRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler
            .HandleAsync(
                new BranchDraftRevisionCommand(OverlayIdentifier.From(overlayIdentifier), op),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: branched => Results.Created(
                $"/overlays/{overlayIdentifier}/revisions/{branched.Value}", branched.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> EditDraft(
        Guid overlayIdentifier,
        int revisionNumber,
        [FromBody] EditDraftRequest body,
        [FromServices] EditDraftRevisionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(body.Label);
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        OverlayRevisionNumber number;
        Label label;
        try
        {
            number = OverlayRevisionNumber.From(revisionNumber);
            label = Label.From(
                body.Label.Text,
                body.Label.NormalizedX,
                body.Label.NormalizedY,
                body.Label.NormalizedWidth,
                body.Label.NormalizedHeight,
                body.Label.FontSizePx);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<OverlayRevisionNumber, EditDraftRevisionError> result = await handler
            .HandleAsync(
                new EditDraftRevisionCommand(OverlayIdentifier.From(overlayIdentifier), number, label),
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
        Guid overlayIdentifier,
        int revisionNumber,
        [FromServices] RevertRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        OverlayRevisionNumber number;
        try
        {
            number = OverlayRevisionNumber.From(revisionNumber);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<OverlayRevisionNumber, RevertRevisionError> result = await handler
            .HandleAsync(
                new RevertRevisionCommand(OverlayIdentifier.From(overlayIdentifier), number, op),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: reverted => Results.Ok(reverted.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
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
/// Envelope shape returned by <c>GET /overlays</c>. Either
/// <see cref="Chains"/> (admin view) or <see cref="Published"/>
/// (binding picker) is populated depending on the state filter; the
/// other is empty.
/// </summary>
public sealed record ListOverlaysResponse(
    IReadOnlyList<OverlayDto> Chains,
    IReadOnlyList<PublishedOverlayDto> Published);
