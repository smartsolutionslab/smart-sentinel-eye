using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Api.Requests;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

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

        return app;
    }

    private static async Task<IResult> Define(
        [FromBody] DefineVariableRequest body,
        [AsParameters] DefineVariableServices services,
        HttpContext http,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(http).IsNotNull();

        var handler = services.Handler;
        var fabGuard = services.FabGuard;

        if (!IdempotencyHeaders.TryRead(http.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        VariableName name;
        VariableType type;
        VariableValue? initialValue = null;
        BooleanLabels? booleanLabels = null;
        try
        {
            name = VariableName.From(body.Name);
            type = VariableType.From(body.Type);
            if (body.InitialValue is { } raw)
            {
                initialValue = VariableValue.From(type, raw);
            }
            if (body.TruthyLabel is not null || body.FalsyLabel is not null)
            {
                booleanLabels = BooleanLabels.From(
                    body.TruthyLabel ?? string.Empty,
                    body.FalsyLabel ?? string.Empty);
            }
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        var actingOperator = user.ToOperatorIdentifier();
        DefineVariableCommand command = new(fab, name, type, initialValue, booleanLabels, actingOperator);

        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(
                    supplied,
                    DefineEndpoint,
                    actingOperator.Value.ToString(),
                    Option<string>.Some(fab.Value),
                    IdempotencyFingerprint.Of(body))),
                services.Idempotency,
                services.Clock),
            _ => $"/system-variables/{name.Value}",
            async token => (await handler.HandleAsync(command, token)).Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084).
    /// </summary>
    private sealed record DefineVariableServices(
        [FromServices] DefineVariableCommandHandler Handler,
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> SetValue(
        string name,
        HttpRequest request,
        [FromBody] SetVariableValueRequest body,
        [FromServices] SetVariableValueCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(body).IsNotNull();

        if (!BoundaryParse.TryParse(
            () => VariableName.From(name),
            "VARIABLE_INVALID_INPUT",
            out var parsed,
            out var problem))
        {
            return problem;
        }

        // Resolved before the precondition, for the same reason as Archive.
        var fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var actingOperator = user.ToOperatorIdentifier();
        // None: an operator setting a value by hand has no plant-floor event at
        // the root of it, so this effect opens no `event → overlay state` leg
        // and the push it causes is correctly left untimed (#2173).
        SetVariableValueCommand command = new(
            fab, parsed, body.Value, actingOperator, Option<int>.Some(expectedVersion), Option<DateTimeOffset>.None);
        var result = await handler.HandleAsync(command, cancellationToken);

        return result.Match<IResult>(onSuccess: identifier => Results.Ok(identifier.Value), onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Archive(
        string name,
        HttpRequest request,
        [FromServices] ArchiveVariableCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        if (!BoundaryParse.TryParse(
            () => VariableName.From(name),
            "VARIABLE_INVALID_INPUT",
            out var parsed,
            out var problem))
        {
            return problem;
        }

        // Resolved before the precondition is read, so a caller who names a fab
        // they do not hold is refused on that ground rather than on a missing
        // If-Match — the reverse order answers 428 to a request that was never
        // theirs to make.
        var fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        var actingOperator = user.ToOperatorIdentifier();
        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var result = await handler.HandleAsync(
            new ArchiveVariableCommand(fab, parsed, actingOperator, expectedVersion), cancellationToken);

        return result.Match<IResult>(onSuccess: identifier => Results.Ok(identifier.Value), onFailure: error => error.ToProblem());
    }

    /// <summary>
    /// SystemVariables' binding of the shared decision table (ADR-0114, as
    /// amended by spec 014) to its own <see cref="FabIdentifier"/>. The table
    /// itself lives in <see cref="FabResolution"/>; this feature adds no
    /// resolution mechanism, it applies the existing one.
    /// </summary>
    private static async Task<Result<FabIdentifier, IResult>> ResolveWriteFabAsync(
        ClaimsPrincipal user, string fabId, IFabAuthorizationGuard fabGuard, CancellationToken cancellationToken)
    {
        var resolution = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "VARIABLE_FAB_REQUIRED", cancellationToken);
        if (resolution.IsFailure)
        {
            return Result<FabIdentifier, IResult>.Failure(resolution.Error);
        }

        try
        {
            return Result<FabIdentifier, IResult>.Success(FabIdentifier.From(resolution.Value));
        }
        catch (ArgumentException ex)
        {
            return Result<FabIdentifier, IResult>.Failure(Results.Problem(
                title: "VARIABLE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }
}
