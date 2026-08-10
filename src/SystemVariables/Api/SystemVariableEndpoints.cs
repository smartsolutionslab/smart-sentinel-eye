using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
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
    public static IEndpointRouteBuilder MapSystemVariableEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder group = app.MapGroup("/system-variables")
            .RequireAuthorization()
            .WithTags("SystemVariables");

        // Reads — any authenticated user.
        group.MapGet("/", List)
            .WithName("ListSystemVariables")
            .Produces<IReadOnlyList<VariableDto>>(StatusCodes.Status200OK);

        group.MapGet("/snapshot", GetSnapshot)
            .WithName("GetOverlaySnapshot")
            .Produces<ResolvedOverlaySnapshotDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{name}", GetOne)
            .WithName("GetSystemVariable")
            .Produces<VariableDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Writes — admin policy.
        group.MapPost("/", Define)
            .RequireAuthorization(Scope.Sse.Variables.Write)
            .WithName("DefineSystemVariable")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{name}/value", SetValue)
            .RequireAuthorization(Scope.Sse.Variables.Write)
            .WithName("SetSystemVariableValue")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{name}/archive", Archive)
            .RequireAuthorization(Scope.Sse.Variables.Write)
            .WithName("ArchiveSystemVariable")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> Define(
        [FromBody] DefineVariableRequest body,
        [FromServices] DefineVariableCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

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

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();

        // Placeholder fab (spec 014 T023 resolves the caller's fab from their
        // group membership). Every variable belongs to munich until then —
        // the same fab T010's backfill attributes every pre-feature row to, so
        // nothing is refused or re-attributed that is not already today.
        FabIdentifier fab = FabIdentifier.From("munich");

        DefineVariableCommand command = new(fab, name, type, initialValue, booleanLabels, actingOperator);
        Result<VariableIdentifier, DefineVariableError> result = await handler.HandleAsync(command, cancellationToken);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Created($"/system-variables/{name.Value}", identifier.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> SetValue(
        string name,
        HttpRequest request,
        [FromBody] SetVariableValueRequest body,
        [FromServices] SetVariableValueCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        if (!BoundaryParse.TryParse(
            () => VariableName.From(name),
            "VARIABLE_INVALID_INPUT",
            out VariableName parsed,
            out IResult problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();

        // Placeholder fab (spec 014 T023 resolves the caller's fab). Every
        // variable is in munich until then, so this addresses the same row it
        // addresses today.
        SetVariableValueCommand command = new(
            FabIdentifier.From("munich"), parsed, body.Value, actingOperator, Option<int>.Some(expectedVersion));
        Result<VariableIdentifier, SetVariableValueError> result = await handler.HandleAsync(command, cancellationToken);

        return result.Match<IResult>(onSuccess: identifier => Results.Ok(identifier.Value), onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetOne(
        string name,
        HttpResponse response,
        [FromServices] GetVariableQueryHandler handler,
        CancellationToken cancellationToken)
    {
        if (!BoundaryParse.TryParse(
            () => VariableName.From(name),
            "VARIABLE_INVALID_INPUT",
            out VariableName parsed,
            out IResult problem))
        {
            return problem;
        }

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(new GetVariableQuery(parsed), cancellationToken);

        return result.Match<IResult>(
            onSuccess: variable =>
            {
                // The version the caller must echo back in If-Match (ADR-0113).
                response.Headers.ETag = ConcurrencyHeaders.ETag(variable.Version);

                return Results.Ok(variable);
            },
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> List([FromQuery] string? state, [FromServices] ListVariablesQueryHandler handler, CancellationToken cancellationToken)
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

        Result<IReadOnlyList<VariableDto>, ListVariablesError> result = await handler.HandleAsync(new ListVariablesQuery(filter), cancellationToken);

        return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetSnapshot([FromQuery] Guid overlayIdentifier, [FromServices] GetOverlaySnapshotQueryHandler handler, CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result = await handler.HandleAsync(new GetOverlaySnapshotQuery(overlayIdentifier), cancellationToken);

        return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Archive(
        string name,
        HttpRequest request,
        [FromServices] ArchiveVariableCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!BoundaryParse.TryParse(
            () => VariableName.From(name),
            "VARIABLE_INVALID_INPUT",
            out VariableName parsed,
            out IResult problem))
        {
            return problem;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        Result<VariableIdentifier, ArchiveVariableError> result = await handler.HandleAsync(new ArchiveVariableCommand(parsed, actingOperator, expectedVersion), cancellationToken);

        return result.Match<IResult>(onSuccess: identifier => Results.Ok(identifier.Value), onFailure: error => error.ToProblem());
    }
}
