using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
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
        ArgumentNullException.ThrowIfNull(app);

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
        ArgumentNullException.ThrowIfNull(body);

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

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<VariableIdentifier, DefineVariableError> result = await handler
            .HandleAsync(
                new DefineVariableCommand(name, type, initialValue, booleanLabels, op),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Created($"/system-variables/{name.Value}", identifier.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> SetValue(
        string name,
        [FromBody] SetVariableValueRequest body,
        [FromServices] SetVariableValueCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        VariableName parsed;
        try
        {
            parsed = VariableName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<VariableIdentifier, SetVariableValueError> result = await handler
            .HandleAsync(
                new SetVariableValueCommand(parsed, body.Value, op),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Ok(identifier.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> GetOne(
        string name,
        [FromServices] GetVariableQueryHandler handler,
        CancellationToken cancellationToken)
    {
        VariableName parsed;
        try
        {
            parsed = VariableName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<VariableDto, GetVariableError> result = await handler
            .HandleAsync(new GetVariableQuery(parsed), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> List(
        [FromQuery] string? state,
        [FromServices] ListVariablesQueryHandler handler,
        CancellationToken cancellationToken)
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

        Result<IReadOnlyList<VariableDto>, ListVariablesError> result = await handler
            .HandleAsync(new ListVariablesQuery(filter), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> GetSnapshot(
        [FromQuery] Guid overlayIdentifier,
        [FromServices] GetOverlaySnapshotQueryHandler handler,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result = await handler
            .HandleAsync(new GetOverlaySnapshotQuery(overlayIdentifier), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static async Task<IResult> Archive(
        string name,
        [FromServices] ArchiveVariableCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        VariableName parsed;
        try
        {
            parsed = VariableName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<VariableIdentifier, ArchiveVariableError> result = await handler
            .HandleAsync(new ArchiveVariableCommand(parsed, op), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Ok(identifier.Value),
            onFailure: error => Results.Problem(
                title: error.Code,
                detail: error.Message,
                statusCode: (int)error.Status));
    }

    private static OperatorIdentifier OperatorFromClaims(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        string? raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out Guid value) && value != Guid.Empty
            ? OperatorIdentifier.From(value)
            : OperatorIdentifier.From(Guid.CreateVersion7());
    }
}
