using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Command (write) handlers for <see cref="WallEndpoints"/>.</summary>
public static partial class WallEndpoints
{
    private static async Task<IResult> CreateWall(
        [FromBody] CreateWallRequest body,
        [AsParameters] CreateWallServices services,
        HttpContext http,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(http).IsNotNull();

        CreateWallCommandHandler handler = services.Handler;
        IFabAuthorizationGuard fabGuard = services.FabGuard;

        if (!IdempotencyHeaders.TryRead(http.Request, out Option<IdempotencyKey> key, out IResult? keyProblem))
        {
            return keyProblem;
        }

        WallName name;
        List<LayoutIdentifier> scenes;
        try
        {
            name = WallName.From(body.Name);
            scenes = [.. body.Scenes.Select(LayoutIdentifier.From)];
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "WALL_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<FabIdentifier, IResult> fabResolution =
            await LayoutEndpoints.ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;
        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();

        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(supplied, CreateEndpoint, actingOperator.Value.ToString())),
                services.Idempotency,
                services.Clock),
            identifier => $"/walls/{identifier}",
            async token => (await handler.HandleAsync(
                    new CreateWallCommand(fab, name, scenes, actingOperator), token)).Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    /// <summary>Bundled with <c>[AsParameters]</c> so the handler keeps a readable signature (ADR-0084).</summary>
    private sealed record CreateWallServices(
        [FromServices] CreateWallCommandHandler Handler,
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> EditScenes(
        Guid wallIdentifier,
        HttpRequest request,
        [FromBody] EditWallScenesRequest body,
        [FromServices] EditWallScenesCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        if (wallIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "WALL_INVALID_INPUT",
                detail: "wallIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        List<LayoutIdentifier> scenes;
        try
        {
            scenes = [.. body.Scenes.Select(LayoutIdentifier.From)];
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "WALL_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await LayoutEndpoints.ResolveCallerFabsAsync(user, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<WallDto, EditWallScenesError> result = await handler.HandleAsync(
            new EditWallScenesCommand(fabs, WallIdentifier.From(wallIdentifier), expectedVersion, scenes, actingOperator),
            cancellationToken);

        return result.Match<IResult>(
            onSuccess: wall => Results.Ok(wall),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Switch(
        Guid wallIdentifier,
        HttpRequest request,
        [FromBody] SwitchWallSceneRequest body,
        [FromServices] SwitchWallSceneCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        if (wallIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "WALL_INVALID_INPUT",
                detail: "wallIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        SceneTarget target;
        try
        {
            target = ParseSceneTarget(body);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "WALL_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await LayoutEndpoints.ResolveCallerFabsAsync(user, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand(fabs, WallIdentifier.From(wallIdentifier), expectedVersion, target, actingOperator),
            cancellationToken);

        return result.Match<IResult>(
            onSuccess: wall => Results.Ok(wall),
            onFailure: error => error.ToProblem());
    }

    /// <summary>
    /// An unknown <c>target</c>, or <c>"layout"</c> without a
    /// <c>layout</c> identifier, throws <see cref="ArgumentException"/> —
    /// caught by the caller and mapped to <c>400 WALL_INVALID_INPUT</c>
    /// (plan.md §4.4).
    /// </summary>
    private static SceneTarget ParseSceneTarget(SwitchWallSceneRequest body) =>
        body.Target switch
        {
            "next" => new SceneTarget.Next(),
            "layout" when body.Layout is { } layout => new SceneTarget.Layout(LayoutIdentifier.From(layout)),
            "layout" => throw new ArgumentException("target 'layout' requires a layout identifier."),
            _ => throw new ArgumentException($"Unknown switch target '{body.Target}'."),
        };
}
