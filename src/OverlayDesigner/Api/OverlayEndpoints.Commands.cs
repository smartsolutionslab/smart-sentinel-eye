using System.Security.Claims;
using SmartSentinelEye.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.OverlayDesigner.Api.Requests;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Api;

/// <summary>Command (write) handlers for <see cref="OverlayEndpoints"/>.</summary>
public static partial class OverlayEndpoints
{
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
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
            onFailure: error => error.ToProblem());
    }
}
