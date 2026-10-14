using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
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

        if (!IdempotencyHeaders.TryRead(http.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        if (!BoundaryParse.TryParse(
            () => ParseCreateWall(body),
            "WALL_INVALID_INPUT",
            out var input,
            out var problem))
        {
            return problem;
        }

        var (name, scenes) = input;

        var fabResolution = await LayoutCompositionFabResolution.ResolveWriteFabAsync(user, fabId, services.FabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;
        var actingOperator = user.ToOperatorIdentifier();
        var command = new CreateWallCommand(fab, name, scenes, actingOperator);

        return await IdempotentRequest.ExecuteCreateAsync(
            CreateExecution(key, command, body, services),
            identifier => $"/walls/{identifier}",
            async token => (await services.Handler.HandleAsync(command, token)).Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    private static (WallName Name, List<LayoutIdentifier> Scenes) ParseCreateWall(CreateWallRequest body) =>
        (WallName.From(body.Name), ParseScenes(body.Scenes));

    private static IdempotentExecution CreateExecution(
        Option<IdempotencyKey> key,
        CreateWallCommand command,
        CreateWallRequest body,
        CreateWallServices services) =>
        new(
            key.Map(supplied => IdempotencyScope.For(
                supplied,
                CreateEndpoint,
                command.By.Value.ToString(),
                Option<string>.Some(command.Fab.Value),
                IdempotencyFingerprint.Of(body))),
            services.Idempotency,
            services.Clock);

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

        if (!BoundaryParse.TryParse(
            () => ParseScenes(body.Scenes),
            "WALL_INVALID_INPUT",
            out var scenes,
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
            async fabs => (await handler.HandleAsync(
                new EditWallScenesCommand(fabs, WallIdentifier.From(wallIdentifier), expectedVersion, scenes, actingOperator),
                cancellationToken)).Match<IResult>(
                onSuccess: wall => Results.Ok(wall),
                onFailure: error => error.ToProblem()),
            cancellationToken);
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

        if (!BoundaryParse.TryParse(
            () => ParseSceneTarget(body),
            "WALL_INVALID_INPUT",
            out var target,
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
            async fabs => (await handler.HandleAsync(
                new SwitchWallSceneCommand(fabs, WallIdentifier.From(wallIdentifier), expectedVersion, target, actingOperator),
                cancellationToken)).Match<IResult>(
                onSuccess: wall => Results.Ok(wall),
                onFailure: error => error.ToProblem()),
            cancellationToken);
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

    /// <summary>
    /// Minimal APIs don't enforce NRT on a record body, so an omitted
    /// <c>scenes</c> field arrives as <see langword="null"/> despite the
    /// request record's non-nullable type. <c>Ensure.That</c> turns that
    /// into <see cref="ArgumentNullException"/> — caught by the callers
    /// above and mapped to <c>400 WALL_INVALID_INPUT</c>, mirroring
    /// <c>LayoutEndpoints.ParseTiles</c>.
    /// </summary>
    private static List<LayoutIdentifier> ParseScenes(IReadOnlyList<Guid> scenes)
    {
        Ensure.That(scenes).IsNotNull();
        return [.. scenes.Select(LayoutIdentifier.From)];
    }
}
