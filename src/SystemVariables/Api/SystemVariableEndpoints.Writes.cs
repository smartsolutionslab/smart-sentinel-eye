using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Api.Requests;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Api;

/// <summary>Write handlers for <see cref="SystemVariableEndpoints"/>.</summary>
public static partial class SystemVariableEndpoints
{
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
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

        if (!IdempotencyHeaders.TryRead(http.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        if (!BoundaryParse.TryParse(() => ParseDefinition(body), "VARIABLE_INVALID_INPUT", out var input, out var problem))
        {
            return problem;
        }

        var (name, type, initialValue, booleanLabels) = input;

        var fabResolution =
            await ResolveWriteFabAsync(user, fabId, services.FabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        var actingOperator = user.ToOperatorIdentifier();
        DefineVariableCommand command = new(fab, name, type, initialValue, booleanLabels, actingOperator);

        return await IdempotentRequest.ExecuteCreateAsync(
            CreateExecution(key, command, body, services),
            _ => $"/system-variables/{name.Value}",
            async token => (await services.Handler.HandleAsync(command, token)).Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    private static (VariableName Name, VariableType Type, VariableValue? InitialValue, BooleanLabels? BooleanLabels) ParseDefinition(
        DefineVariableRequest body)
    {
        var name = VariableName.From(body.Name);
        var type = VariableType.From(body.Type);
        VariableValue? initialValue = null;
        BooleanLabels? booleanLabels = null;
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

        return (name, type, initialValue, booleanLabels);
    }

    private static IdempotentExecution CreateExecution(
        Option<IdempotencyKey> key,
        DefineVariableCommand command,
        DefineVariableRequest body,
        DefineVariableServices services) =>
        new(
            key.Map(supplied => IdempotencyScope.For(
                supplied,
                DefineEndpoint,
                command.DefinedBy.Value.ToString(),
                Option<string>.Some(command.Fab.Value),
                IdempotencyFingerprint.Of(body))),
            services.Idempotency,
            services.Clock);

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084).
    /// </summary>
    private sealed record DefineVariableServices(
        [FromServices] DefineVariableCommandHandler Handler,
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
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

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
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
