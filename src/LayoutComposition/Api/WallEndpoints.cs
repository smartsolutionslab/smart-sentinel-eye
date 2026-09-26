using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>
/// Minimal-API endpoint group for walls (spec 258 US1, plan.md §4.4): a
/// wall, its scene set, and switching by hand. Split across partial files
/// by message kind, mirroring <see cref="LayoutEndpoints"/>.
/// </summary>
public static partial class WallEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string CreateEndpoint = "POST /walls";

    public static IEndpointRouteBuilder MapWallEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder group = app.MapGroup("/walls")
            .WithTags("Walls");

        group.MapPost("/", CreateWall)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("CreateWall")
            .WithSummary(
                "Create a new wall in the resolved fab, showing its first scene. "
                + "Required scope: sse.layouts.write")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{wallIdentifier:guid}", GetOne)
            .RequireAuthorization(Scope.Sse.Layouts.Read)
            .WithName("GetWall")
            .WithSummary(
                "Read one wall by its identifier, within your fabs. "
                + "Required scope: sse.layouts.read")
            .Produces<WallDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Layouts.Read)
            .WithName("ListWalls")
            .WithSummary("List walls in your fabs. Required scope: sse.layouts.read")
            .Produces<IReadOnlyList<WallDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{wallIdentifier:guid}/scenes", EditScenes)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("EditWallScenes")
            .WithSummary(
                "Replace a wall's scene set. Moves Showing to the new first scene if it was "
                + "dropped (US1-15). Required scope: sse.layouts.write")
            .Produces<WallDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{wallIdentifier:guid}/switch", Switch)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("SwitchWallScene")
            .WithSummary(
                "Switch a wall to its next scene or a named one. A switch that leaves Showing "
                + "unchanged is a no-op. Required scope: sse.layouts.write")
            .Produces<WallDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
