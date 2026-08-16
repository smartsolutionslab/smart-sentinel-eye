using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Command (write) handlers for <see cref="LayoutEndpoints"/>.</summary>
public static partial class LayoutEndpoints
{
    /// <summary>
    /// LayoutComposition's binding of the shared decision table (ADR-0114) to
    /// its own <see cref="FabIdentifier"/>. The table itself lives in
    /// <see cref="FabResolution"/>; this feature adds no resolution mechanism,
    /// it applies the existing one (spec 017).
    ///
    /// <para>
    /// Only <c>POST /layouts</c> uses this. The other five writes address an
    /// existing layout, which already has a fab — letting the caller name one
    /// there would allow the two to disagree.
    /// </para>
    /// </summary>
    private static async Task<(FabIdentifier? Fab, IResult? Problem)> ResolveWriteFabAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        (string resolved, IResult problem) = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "LAYOUT_FAB_REQUIRED", cancellationToken);
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
                title: "LAYOUT_INVALID_INPUT", detail: ex.Message,
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
    /// whole read, hiding every layout in the fabs the caller legitimately
    /// holds. Mirrors <c>CameraEndpoints</c>, where that was a real defect.
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
                title: "LAYOUT_FAB_REQUIRED",
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return (fabs, null);
    }

    private static async Task<IResult> CreateDraft(
        [FromBody] CreateLayoutRequest body,
        [FromServices] CreateLayoutDraftCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(body).IsNotNull();

        LayoutName name;
        GridDimensions grid;
        IReadOnlyList<Tile> tiles;
        try
        {
            name = LayoutName.From(body.Name);
            grid = GridDimensions.From(body.Grid.Rows, body.Grid.Cols);
            tiles = ParseTiles(body.Tiles);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        (FabIdentifier? fab, IResult? fabProblem) =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fab is null)
        {
            return fabProblem!;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler
            .HandleAsync(new CreateLayoutDraftCommand(fab, name, grid, tiles, actingOperator), cancellationToken);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Created($"/layouts/{identifier.Value}", identifier.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Publish(
        Guid layoutIdentifier,
        int revisionNumber,
        HttpRequest request,
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
        if (!BoundaryParse.TryParse(
            () => LayoutRevisionNumber.From(revisionNumber),
            "LAYOUT_INVALID_INPUT",
            out LayoutRevisionNumber number,
            out IResult problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler
            .HandleAsync(
                new PublishRevisionCommand(LayoutIdentifier.From(layoutIdentifier), number, actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: published => Results.Ok(published.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Archive(
        Guid layoutIdentifier,
        int revisionNumber,
        HttpRequest request,
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
        if (!BoundaryParse.TryParse(
            () => LayoutRevisionNumber.From(revisionNumber),
            "LAYOUT_INVALID_INPUT",
            out LayoutRevisionNumber number,
            out IResult problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<LayoutRevisionNumber, ArchiveRevisionError> result = await handler
            .HandleAsync(
                new ArchiveRevisionCommand(LayoutIdentifier.From(layoutIdentifier), number, actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: archived => Results.Ok(archived.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> BranchDraft(
        Guid layoutIdentifier,
        HttpRequest request,
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

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler
            .HandleAsync(
                new BranchDraftRevisionCommand(LayoutIdentifier.From(layoutIdentifier), actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: branched => Results.Created(
                $"/layouts/{layoutIdentifier}/revisions/{branched.Value}", branched.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> EditDraft(
        Guid layoutIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromBody] EditDraftRequest body,
        [FromServices] EditDraftRevisionCommandHandler handler,
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
        LayoutRevisionNumber number;
        GridDimensions grid;
        IReadOnlyList<Tile> tiles;
        try
        {
            number = LayoutRevisionNumber.From(revisionNumber);
            grid = GridDimensions.From(body.Grid.Rows, body.Grid.Cols);
            tiles = ParseTiles(body.Tiles);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler
            .HandleAsync(
                new EditDraftRevisionCommand(LayoutIdentifier.From(layoutIdentifier), number, grid, tiles, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: edited => Results.Ok(edited.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Revert(
        Guid layoutIdentifier,
        int revisionNumber,
        HttpRequest request,
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
        if (!BoundaryParse.TryParse(
            () => LayoutRevisionNumber.From(revisionNumber),
            "LAYOUT_INVALID_INPUT",
            out LayoutRevisionNumber number,
            out IResult problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<LayoutRevisionNumber, RevertRevisionError> result = await handler
            .HandleAsync(
                new RevertRevisionCommand(LayoutIdentifier.From(layoutIdentifier), number, actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: reverted => Results.Ok(reverted.Value),
            onFailure: error => error.ToProblem());
    }

    private static List<Tile> ParseTiles(IReadOnlyList<TileRequest> requests)
    {
        Ensure.That(requests).IsNotNull();
        return requests
            .Select(request => new Tile(
                CameraIdentifier.From(request.CameraIdentifier),
                request.OverlayIdentifier is { } overlayId
                    ? Option<OverlayIdentifier>.Some(OverlayIdentifier.From(overlayId))
                    : Option<OverlayIdentifier>.None,
                GridPosition.From(request.Row, request.Col)))
            .ToList();
    }
}
