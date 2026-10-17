using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.Automation.Api.Requests;
using SmartSentinelEye.Automation.Application.Queries;
using SmartSentinelEye.Automation.Application.Queries.Handlers;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Api;

/// <summary>Read handlers for <see cref="RulesEndpoints"/>.</summary>
public static partial class RulesEndpoints
{
    // The three filters are nullable because they are optional, and in a
    // project with NRT on that is the difference between an absent filter and
    // a rejected request: minimal APIs treat a non-nullable parameter with no
    // default as required, so a plain `GET /rules` never reached the handler.
    // It surfaced as a 500 rather than a 400 because the binding failure is an
    // exception, and UseExceptionHandler turns anything it does not recognise
    // into a generic problem response (#1298).
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> List(
        [FromQuery] string? state,
        [FromQuery] string? triggerSource,
        [FromQuery] string? triggerKind,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] ListRulesQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler.HandleAsync(
            new ListRulesQuery(fabs, state, triggerSource, triggerKind), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> GetOne(
        string name,
        HttpResponse response,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] GetRuleQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler.HandleAsync(
            new GetRuleQuery(fabs, name), cancellationToken);

        return result.Match<IResult>(
            onSuccess: rule =>
            {
                // The version the caller must echo back in If-Match (ADR-0113).
                response.Headers.ETag = ConcurrencyHeaders.ETag(rule.Version);

                return Results.Ok(rule);
            },
            onFailure: error => error.ToProblem());
    }

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> DryRun(
        string name,
        [FromBody] DryRunRuleRequest body,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] DryRunRuleQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(body).IsNotNull();

        // Guarded like the reads: a trial run must not be a side channel for
        // discovering another fab's rule behaviour (spec 013 FR-006).
        var fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler.HandleAsync(
            new DryRunRuleQuery(fabs, name, body.SampleEvent), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
        ClaimsPrincipal user, string fabId, IFabAuthorizationGuard fabGuard, CancellationToken cancellationToken)
    {
        var resolved = await FabResolution.ResolveForReadAsync(
            user, fabId, fabGuard, cancellationToken);

        // Per entry, not all-or-nothing. A single group under /fabs/ that is
        // not a usable fab name — a sub-group, or a name outside Automation's
        // grammar — used to fail the whole read, hiding every rule in the fabs
        // the caller legitimately holds. One odd group should cost them that
        // group, not all of them.
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
                // configuration. If *nothing* is usable the request still
                // fails below, so a wholly-malformed group set is not silent.
            }
        }

        if (fabs.Count == 0)
        {
            return Result<IReadOnlyList<FabIdentifier>, IResult>.Failure(Results.Problem(
                title: "RULE_INVALID_INPUT",
                detail: "None of your fab group memberships is a usable fab identifier.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return Result<IReadOnlyList<FabIdentifier>, IResult>.Success(fabs);
    }
}
