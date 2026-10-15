using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.OverlayDesigner.Api.Requests;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Api;

/// <summary>Command (write) handlers for <see cref="OverlayEndpoints"/>.</summary>
public static partial class OverlayEndpoints
{
    private static async Task<IResult> CreateDraft(
        [FromBody] CreateOverlayRequest body,
        [AsParameters] CreateOverlayServices services,
        HttpContext http,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(http).IsNotNull();

        if (!IdempotencyHeaders.TryRead(http.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        if (!BoundaryParse.TryParse(() => ParseCreateOverlay(body), "OVERLAY_INVALID_INPUT", out var input, out var problem))
        {
            return problem;
        }

        var (name, elements) = input;

        var command = new CreateOverlayDraftCommand(name, elements, user.ToOperatorIdentifier());

        return await IdempotentRequest.ExecuteCreateAsync(
            CreateExecution(key, command, body, services),
            identifier => $"/overlays/{identifier}",
            async token => (await services.Handler.HandleAsync(command, token)).Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    private static (OverlayName Name, List<OverlayElement> Elements) ParseCreateOverlay(CreateOverlayRequest body)
    {
        Ensure.That(body.Elements).IsNotNull();

        return (OverlayName.From(body.Name), [.. body.Elements.Select(OverlayElementParsing.ParseElement)]);
    }

    private static IdempotentExecution CreateExecution(
        Option<IdempotencyKey> key,
        CreateOverlayDraftCommand command,
        CreateOverlayRequest body,
        CreateOverlayServices services) =>
        new(
            key.Map(supplied => IdempotencyScope.For(
                supplied,
                CreateEndpoint,
                command.CreatedBy.Value.ToString(),
                Option<string>.None,
                IdempotencyFingerprint.Of(body))),
            services.Idempotency,
            services.Clock);

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084).
    /// </summary>
    private sealed record CreateOverlayServices(
        [FromServices] CreateOverlayDraftCommandHandler Handler,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> Publish(
        Guid overlayIdentifier,
        int revisionNumber,
        HttpRequest request,
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
        if (!BoundaryParse.TryParse(
            () => OverlayRevisionNumber.From(revisionNumber),
            "OVERLAY_INVALID_INPUT",
            out var number,
            out var problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var actingOperator = user.ToOperatorIdentifier();
        var publishRevisionCommand = new PublishRevisionCommand(
            OverlayIdentifier.From(overlayIdentifier),
            number,
            actingOperator,
            expectedVersion);
        var result = await handler.HandleAsync(publishRevisionCommand, cancellationToken);

        return result.Match<IResult>(
            onSuccess: published => Results.Ok(published.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Archive(
        Guid overlayIdentifier,
        int revisionNumber,
        HttpRequest request,
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
        if (!BoundaryParse.TryParse(
            () => OverlayRevisionNumber.From(revisionNumber),
            "OVERLAY_INVALID_INPUT",
            out var number,
            out var problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var actingOperator = user.ToOperatorIdentifier();
        var archiveRevisionCommand = new ArchiveRevisionCommand(
            OverlayIdentifier.From(overlayIdentifier),
            number,
            actingOperator,
            expectedVersion);
        var result = await handler.HandleAsync(archiveRevisionCommand, cancellationToken);

        return result.Match<IResult>(
            onSuccess: archived => Results.Ok(archived.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> BranchDraft(
        Guid overlayIdentifier,
        HttpRequest request,
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

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var actingOperator = user.ToOperatorIdentifier();
        var branchDraftRevisionCommand = new BranchDraftRevisionCommand(
            OverlayIdentifier.From(overlayIdentifier),
            actingOperator,
            expectedVersion);
        var result = await handler.HandleAsync(branchDraftRevisionCommand, cancellationToken);

        return result.Match<IResult>(
            onSuccess: branched => Results.Created(
                $"/overlays/{overlayIdentifier}/revisions/{branched.Value}", branched.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> EditDraft(
        Guid overlayIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromBody] EditDraftRequest body,
        [FromServices] EditDraftRevisionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (!BoundaryParse.TryParse(
            () => ParseDraftEdit(revisionNumber, body),
            "OVERLAY_INVALID_INPUT",
            out var input,
            out var problem))
        {
            return problem;
        }

        var (number, elements) = input;

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var editDraftRevisionCommand = new EditDraftRevisionCommand(
            OverlayIdentifier.From(overlayIdentifier),
            number,
            elements,
            expectedVersion);
        var result = await handler.HandleAsync(editDraftRevisionCommand, cancellationToken);

        return result.Match<IResult>(
            onSuccess: edited => Results.Ok(edited.Value),
            onFailure: error => error.ToProblem());
    }

    private static (OverlayRevisionNumber Number, List<OverlayElement> Elements) ParseDraftEdit(
        int revisionNumber,
        EditDraftRequest body)
    {
        Ensure.That(body.Elements).IsNotNull();

        return (OverlayRevisionNumber.From(revisionNumber), [.. body.Elements.Select(OverlayElementParsing.ParseElement)]);
    }

    private static async Task<IResult> Revert(
        Guid overlayIdentifier,
        int revisionNumber,
        HttpRequest request,
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
        if (!BoundaryParse.TryParse(
            () => OverlayRevisionNumber.From(revisionNumber),
            "OVERLAY_INVALID_INPUT",
            out var number,
            out var problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var actingOperator = user.ToOperatorIdentifier();

        var revertRevisionCommand = new RevertRevisionCommand(
            OverlayIdentifier.From(overlayIdentifier),
            number,
            actingOperator,
            expectedVersion);
        var result = await handler.HandleAsync(revertRevisionCommand, cancellationToken);

        return result.Match<IResult>(
            onSuccess: reverted => Results.Ok(reverted.Value),
            onFailure: error => error.ToProblem());
    }
}
