using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Command (write) handlers for <see cref="LayoutEndpoints"/>.</summary>
public static partial class LayoutEndpoints
{
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> CreateDraft(
        [FromBody] CreateLayoutRequest body,
        [AsParameters] CreateLayoutServices services,
        HttpContext http,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(http).IsNotNull();

        if (!IdempotencyHeaders.TryRead(http.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        if (!BoundaryParse.TryParse(
            () => ParseCreateLayout(body),
            "LAYOUT_INVALID_INPUT",
            out var input,
            out var problem))
        {
            return problem;
        }

        var (name, grid, tiles) = input;

        var fabResolution = await LayoutCompositionFabResolution.ResolveWriteFabAsync(user, fabId, services.FabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;
        var actingOperator = user.ToOperatorIdentifier();
        var command = new CreateLayoutDraftCommand(fab, name, grid, tiles, actingOperator);

        return await IdempotentRequest.ExecuteCreateAsync(
            CreateExecution(key, command, body, services),
            identifier => $"/layouts/{identifier}",
            async token => (await services.Handler.HandleAsync(command, token)).Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    private static (LayoutName Name, GridDimensions Grid, IReadOnlyList<Tile> Tiles) ParseCreateLayout(CreateLayoutRequest body) =>
        (LayoutName.From(body.Name), LayoutRequestParsing.ParseGrid(body.Grid), LayoutRequestParsing.ParseTiles(body.Tiles));

    private static IdempotentExecution CreateExecution(
        Option<IdempotencyKey> key,
        CreateLayoutDraftCommand command,
        CreateLayoutRequest body,
        CreateLayoutServices services) =>
        new(
            key.Map(supplied => IdempotencyScope.For(
                supplied,
                CreateEndpoint,
                command.CreatedBy.Value.ToString(),
                Option<string>.Some(command.Fab.Value),
                IdempotencyFingerprint.Of(body))),
            services.Idempotency,
            services.Clock);

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084).
    /// </summary>
    private sealed record CreateLayoutServices(
        [FromServices] CreateLayoutDraftCommandHandler Handler,
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> Publish(
        Guid layoutIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromServices] PublishRevisionCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
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
        if (!BoundaryParse.TryParse(
            () => LayoutRevisionNumber.From(revisionNumber),
            "LAYOUT_INVALID_INPUT",
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

        return await LayoutCompositionFabResolution.ForCallerFabsAsync(
            user,
            fabGuard,
            async fabs => RevisionOutcome(await handler.HandleAsync(
                new PublishRevisionCommand(fabs, LayoutIdentifier.From(layoutIdentifier), number, actingOperator, expectedVersion),
                cancellationToken)),
            cancellationToken);
    }

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> Archive(
        Guid layoutIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromServices] ArchiveRevisionCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
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
        if (!BoundaryParse.TryParse(
            () => LayoutRevisionNumber.From(revisionNumber),
            "LAYOUT_INVALID_INPUT",
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

        return await LayoutCompositionFabResolution.ForCallerFabsAsync(
            user,
            fabGuard,
            async fabs => RevisionOutcome(await handler.HandleAsync(
                new ArchiveRevisionCommand(fabs, LayoutIdentifier.From(layoutIdentifier), number, actingOperator, expectedVersion),
                cancellationToken)),
            cancellationToken);
    }

    private static async Task<IResult> BranchDraft(
        Guid layoutIdentifier,
        HttpRequest request,
        [FromServices] BranchDraftRevisionCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
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

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var actingOperator = user.ToOperatorIdentifier();
        var fabsResolution =
            await LayoutCompositionFabResolution.ResolveCallerFabsAsync(user, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler
            .HandleAsync(
                new BranchDraftRevisionCommand(fabs, LayoutIdentifier.From(layoutIdentifier), actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: branched => Results.Created(
                $"/layouts/{layoutIdentifier}/revisions/{branched.Value}", branched.Value),
            onFailure: error => error.ToProblem());
    }

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> EditDraft(
        Guid layoutIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromBody] EditDraftRequest body,
        [FromServices] EditDraftRevisionCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (!BoundaryParse.TryParse(
            () => ParseDraftEdit(revisionNumber, body),
            "LAYOUT_INVALID_INPUT",
            out var input,
            out var problem))
        {
            return problem;
        }

        var (number, grid, tiles) = input;

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        return await LayoutCompositionFabResolution.ForCallerFabsAsync(
            user,
            fabGuard,
            async fabs => RevisionOutcome(await handler.HandleAsync(
                new EditDraftRevisionCommand(fabs, LayoutIdentifier.From(layoutIdentifier), number, grid, tiles, expectedVersion),
                cancellationToken)),
            cancellationToken);
    }

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> Revert(
        Guid layoutIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromServices] RevertRevisionCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
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
        if (!BoundaryParse.TryParse(
            () => LayoutRevisionNumber.From(revisionNumber),
            "LAYOUT_INVALID_INPUT",
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

        return await LayoutCompositionFabResolution.ForCallerFabsAsync(
            user,
            fabGuard,
            async fabs => RevisionOutcome(await handler.HandleAsync(
                new RevertRevisionCommand(fabs, LayoutIdentifier.From(layoutIdentifier), number, actingOperator, expectedVersion),
                cancellationToken)),
            cancellationToken);
    }

    private static (LayoutRevisionNumber Number, GridDimensions Grid, IReadOnlyList<Tile> Tiles) ParseDraftEdit(
        int revisionNumber,
        EditDraftRequest body) =>
        (LayoutRevisionNumber.From(revisionNumber), LayoutRequestParsing.ParseGrid(body.Grid), LayoutRequestParsing.ParseTiles(body.Tiles));

    private static IResult RevisionOutcome<TError>(Result<LayoutRevisionNumber, TError> result)
        where TError : ApiError =>
        result.Match<IResult>(
            onSuccess: number => Results.Ok(number.Value),
            onFailure: error => error.ToProblem());
}
