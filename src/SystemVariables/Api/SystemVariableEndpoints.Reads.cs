using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Api;

/// <summary>Read handlers for <see cref="SystemVariableEndpoints"/>.</summary>
public static partial class SystemVariableEndpoints
{
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
            out var problem))
        {
            return problem;
        }

        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler.HandleAsync(
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

        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler.HandleAsync(
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

        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler.HandleAsync(
            new GetOverlaySnapshotQuery(fabs, overlayIdentifier), cancellationToken);

        return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> ResolveText(
        [FromQuery] string? text,
        [FromServices] ResolveOverlayTextQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        if (string.IsNullOrEmpty(text))
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: "text must not be empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (text.Length > MaximumTextLength)
        {
            return Results.Problem(
                title: "VARIABLE_INVALID_INPUT",
                detail: $"text must not exceed {MaximumTextLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler.HandleAsync(
            new ResolveOverlayTextQuery(fabs, text), cancellationToken);

        return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
    }

    private static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
        ClaimsPrincipal user, string fabId, IFabAuthorizationGuard fabGuard, CancellationToken cancellationToken)
    {
        var resolved = await FabResolution.ResolveForReadAsync(
            user, fabId, fabGuard, cancellationToken);

        // Per entry, not all-or-nothing. A single group under /fabs/ that is
        // not a usable fab name — a sub-group, or a name outside this context's
        // grammar — would otherwise fail the whole read, hiding every variable
        // in the fabs the caller legitimately holds. Mirrors RulesEndpoints,
        // where that was a real defect.
        List<FabIdentifier> fabs = [];
        foreach (var candidate in resolved)
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
