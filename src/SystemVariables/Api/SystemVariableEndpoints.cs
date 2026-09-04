using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Api.Requests;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Api;

/// <summary>
/// Minimal-API endpoint group for SystemVariables (ADR-0070). Spec 005
/// US1/US2/US3 — Define / SetValue / GetSnapshot. Archive lands in PR F.
/// Writes require admin policy; reads require any authenticated user.
/// </summary>
public static class SystemVariableEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string DefineEndpoint = "POST /system-variables";

    public static IEndpointRouteBuilder MapSystemVariableEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder group = app.MapGroup("/system-variables")
            .RequireAuthorization()
            .WithTags("SystemVariables");

        // Reads — any authenticated user. 400 and 403 became reachable on every
        // one of these when fab resolution landed (spec 014 T029): 403 when the
        // caller names a fab they lack or holds none, 400 when their groups
        // yield no usable fab name. Declaring them keeps the generated OpenAPI
        // from claiming they cannot happen.
        group.MapGet("/", List)
            .WithName("ListSystemVariables")
            .WithSummary(
                "List variables in your fabs. Omit fabId to span all of them; name one to narrow (spec 014). "
                + "Archived variables are excluded unless includeArchived=true, or state=Archived asks for them "
                + "outright — the same shape GET /cameras uses for retired ones. Every row carries its state.")
            .Produces<IReadOnlyList<VariableDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/snapshot", GetSnapshot)
            .WithName("GetOverlaySnapshot")
            .Produces<ResolvedOverlaySnapshotDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{name}", GetOne)
            .WithName("GetSystemVariable")
            .WithSummary("Read one variable within your fabs. 400 if the name is held in more than one and none is named.")
            .Produces<VariableDto>(StatusCodes.Status200OK)
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
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{name}/value", SetValue)
            .RequireAuthorization(Scope.Sse.Variables.Write)
            .WithName("SetSystemVariableValue")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{name}/archive", Archive)
            .RequireAuthorization(Scope.Sse.Variables.Write)
            .WithName("ArchiveSystemVariable")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

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

        DefineVariableCommandHandler handler = services.Handler;
        IFabAuthorizationGuard fabGuard = services.FabGuard;

        if (!IdempotencyHeaders.TryRead(http.Request, out Option<IdempotencyKey> key, out IResult? keyProblem))
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

        Result<FabIdentifier, IResult> fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        DefineVariableCommand command = new(fab, name, type, initialValue, booleanLabels, actingOperator);

        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(supplied, DefineEndpoint, actingOperator.Value.ToString())),
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
            out IResult? problem))
        {
            return problem;
        }

        // Resolved before the precondition, for the same reason as Archive.
        Result<FabIdentifier, IResult> fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        SetVariableValueCommand command = new(
            fab, parsed, body.Value, actingOperator, Option<int>.Some(expectedVersion));
        Result<VariableIdentifier, SetVariableValueError> result = await handler.HandleAsync(command, cancellationToken);

        return result.Match<IResult>(onSuccess: identifier => Results.Ok(identifier.Value), onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetOne(
        string name,
        HttpResponse response,
        [FromServices] GetVariableQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        if (!BoundaryParse.TryParse(
            () => VariableName.From(name),
            "VARIABLE_INVALID_INPUT",
            out var parsed,
            out IResult? problem))
        {
            return problem;
        }

        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery(fabs, parsed), cancellationToken);

        return result.Match<IResult>(
            onSuccess: variable =>
            {
                // The version the caller must echo back in If-Match (ADR-0113).
                response.Headers.ETag = ConcurrencyHeaders.ETag(variable.Version);

                return Results.Ok(variable);
            },
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> List(
        [FromQuery] string? state,
        [FromServices] ListVariablesQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "",
        // Defaulted, not merely optional: a non-nullable value type with no
        // default is a *required* query parameter to minimal APIs, and the
        // listing this endpoint exists to serve sends no query string at all.
        [FromQuery] bool includeArchived = false)
    {
        VariableState? filter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            try
            {
                filter = VariableState.From(state);
            }
            catch (ArgumentException)
            {
                return Results.Problem(
                    title: "VARIABLE_INVALID_STATE_FILTER",
                    detail: $"'{state}' is not a valid variable state (Defined | Archived).",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<IReadOnlyList<VariableDto>, ListVariablesError> result = await handler.HandleAsync(
            new ListVariablesQuery(fabs, filter, includeArchived), cancellationToken);

        return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetSnapshot(
        [FromQuery] Guid overlayIdentifier,
        [FromServices] GetOverlaySnapshotQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result = await handler.HandleAsync(
            new GetOverlaySnapshotQuery(fabs, overlayIdentifier), cancellationToken);

        return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
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
            out IResult? problem))
        {
            return problem;
        }

        // Resolved before the precondition is read, so a caller who names a fab
        // they do not hold is refused on that ground rather than on a missing
        // If-Match — the reverse order answers 428 to a request that was never
        // theirs to make.
        Result<FabIdentifier, IResult> fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        Result<VariableIdentifier, ArchiveVariableError> result = await handler.HandleAsync(
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
        Result<string, IResult> resolution = await FabResolution.ResolveForWriteAsync(
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

    private static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
        ClaimsPrincipal user, string fabId, IFabAuthorizationGuard fabGuard, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> resolved = await FabResolution.ResolveForReadAsync(
            user, fabId, fabGuard, cancellationToken);

        // Per entry, not all-or-nothing. A single group under /fabs/ that is
        // not a usable fab name — a sub-group, or a name outside this context's
        // grammar — would otherwise fail the whole read, hiding every variable
        // in the fabs the caller legitimately holds. Mirrors RulesEndpoints,
        // where that was a real defect.
        List<FabIdentifier> fabs = [];
        foreach (string candidate in resolved)
        {
            try
            {
                fabs.Add(FabIdentifier.From(candidate));
            }
            catch (ArgumentException)
            {
                // Skipped, not reported: there is no logger at this layer, and
                // a caller cannot act on a message about someone else's group
                // configuration. If nothing is usable the request still fails
                // below, so a wholly-malformed group set is not silent.
            }
        }

        if (fabs.Count == 0)
        {
            return Result<IReadOnlyList<FabIdentifier>, IResult>.Failure(Results.Problem(
                title: "VARIABLE_FAB_REQUIRED",
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return Result<IReadOnlyList<FabIdentifier>, IResult>.Success(fabs);
    }
}
