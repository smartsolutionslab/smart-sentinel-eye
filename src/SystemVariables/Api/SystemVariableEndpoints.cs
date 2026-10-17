using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;

namespace SmartSentinelEye.SystemVariables.Api;

/// <summary>
/// Minimal-API endpoint group for SystemVariables (ADR-0070). Spec 005
/// US1/US2/US3 — Define / SetValue / GetSnapshot. Archive lands in PR F.
/// Writes require <c>sse.variables.write</c>; reads require
/// <c>sse.variables.read</c> (spec 073, issue 2070 — which retires spec 005
/// FR-021's "reads require any authenticated user").
/// </summary>
public static partial class SystemVariableEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string DefineEndpoint = "POST /system-variables";

    /// <summary>
    /// Spec 148's own bound, chosen to match the <c>Label</c> value object's
    /// 256-character limit (<c>overlays.schema.ts:7</c>) — not a reference to
    /// it, since OverlayDesigner and SystemVariables share no project
    /// reference. The server does not resolve text no overlay could ever
    /// carry.
    /// </summary>
    private const int MaximumTextLength = 256;

    public static IEndpointRouteBuilder MapSystemVariableEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        var group = app.MapGroup("/system-variables")
            .RequireAuthorization()
            .WithTags("SystemVariables");

        MapListSystemVariables(group);
        MapGetOverlaySnapshot(group);
        MapResolveOverlayText(group);
        MapGetSystemVariable(group);
        MapDefineSystemVariable(group);
        MapSetSystemVariableValue(group);
        MapArchiveSystemVariable(group);

        return app;
    }

    private static void MapListSystemVariables(RouteGroupBuilder group)
    {
        // Reads — the read scope. 400 and 403 became reachable on every
        // one of these when fab resolution landed (spec 014 T029): 403 when the
        // caller names a fab they lack or holds none, 400 when their groups
        // yield no usable fab name. Declaring them keeps the generated OpenAPI
        // from claiming they cannot happen.
        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Variables.Read)
            .WithName("ListSystemVariables")
            .WithSummary(
                "List variables in your fabs. Omit fabId to span all of them; name one to narrow (spec 014). "
                + "Archived variables are excluded unless includeArchived=true, or state=Archived asks for them "
                + "outright — the same shape GET /cameras uses for retired ones. Every row carries its state. "
                + "Required scope: sse.variables.read")
            .Produces<IReadOnlyList<VariableDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static void MapGetOverlaySnapshot(RouteGroupBuilder group)
    {
        group.MapGet("/-/snapshot", GetSnapshot)
            .RequireAuthorization(Scope.Sse.Variables.Read)
            .WithName("GetOverlaySnapshot")
            .WithSummary(
                "Read one overlay's label text with every variable placeholder already resolved to the value "
                + "current in your fabs — the opening label a kiosk tile renders when it attaches, before any "
                + "live update arrives. 404 if the overlay is unknown to your fabs. "
                + "Required scope: sse.variables.read")
            .Produces<ResolvedOverlaySnapshotDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapResolveOverlayText(RouteGroupBuilder group)
    {
        group.MapGet("/-/resolve", ResolveText)
            .RequireAuthorization(Scope.Sse.Variables.Read)
            .WithName("ResolveOverlayText")
            .WithSummary(
                "Resolve arbitrary text carrying {{variable}} placeholders against your fabs — the overlay editor's "
                + "live preview of what the wall will render, using the exact same resolution rules GetOverlaySnapshot "
                + "uses. Unresolvable placeholders (unknown, unset, or archived) keep their literal {{name}} in the "
                + "resolved text (FR-011); the per-name entries in the response are what distinguish those three cases "
                + "from each other and from a resolved one. No 404 — resolving text with no matching variables is a 200 "
                + "with an empty placeholders list, since there is no resource to be absent. "
                + "Required scope: sse.variables.read")
            .Produces<ResolvedTextPreviewDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static void MapGetSystemVariable(RouteGroupBuilder group)
    {
        group.MapGet("/{name}", GetOne)
            .RequireAuthorization(Scope.Sse.Variables.Read)
            .WithName("GetSystemVariable")
            .WithSummary(
                "Read one variable within your fabs. 400 if the name is held in more than one and none is named. "
                + "Required scope: sse.variables.read")
            .Produces<VariableDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapDefineSystemVariable(RouteGroupBuilder group)
    {
        // Writes — admin policy. A caller in exactly one fab may omit fabId; a
        // caller in several must name it. See ADR-0114, amended by spec 014.
        group.MapPost("/", Define)
            .RequireAuthorization(Scope.Sse.Variables.Write)
            .WithName("DefineSystemVariable")
            .WithSummary("Define a variable in the resolved fab. Required scope: sse.variables.write")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static void MapSetSystemVariableValue(RouteGroupBuilder group)
    {
        group.MapPut("/{name}/value", SetValue)
            .RequireAuthorization(Scope.Sse.Variables.Write)
            .WithName("SetSystemVariableValue")
            .WithSummary("Set a variable's value in the resolved fab. Required scope: sse.variables.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
    }

    private static void MapArchiveSystemVariable(RouteGroupBuilder group)
    {
        group.MapPost("/{name}/archive", Archive)
            .RequireAuthorization(Scope.Sse.Variables.Write)
            .WithName("ArchiveSystemVariable")
            .WithSummary("Archive a variable in the resolved fab. Required scope: sse.variables.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
    }
}
