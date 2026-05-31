using System.Security.Claims;
using SmartSentinelEye.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Command (write) handlers for <see cref="LayoutEndpoints"/>.</summary>
public static partial class LayoutEndpoints
{
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
    }

    private static OverlayChange TranslateOverlayChange(OverlayBindingUpdate? update)
    {
        if (update is null) return OverlayChange.None;
        if (update.Identifier is { } overlayId) return OverlayChange.Set(OverlayIdentifier.From(overlayId));
        return OverlayChange.Clear();
    }
}
