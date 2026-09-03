using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.Automation.Api.Requests;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Application.Queries;
using SmartSentinelEye.Automation.Application.Queries.Handlers;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Api;

/// <summary>
/// Minimal-API endpoints for Automation rules (spec 007 / ADR-0070).
/// All writes require <see cref="AuthenticationDefaults.AdminPolicy"/>;
/// read endpoints land in PR F (polish) along with the dry-run path.
/// </summary>
public static class RulesEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string CreateEndpoint = "POST /rules";

    private const string SetVariableValue = "SetVariableValue";
    private const string HighlightOverlay = "HighlightOverlay";

    public static IEndpointRouteBuilder MapRulesEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder group = app.MapGroup("/rules")
            .RequireAuthorization(Scope.Sse.Rules.Write)
            .WithTags("Rules");

        group.MapPost("/", Create)
            .WithName("CreateRule")
            .WithSummary("Author a rule. Omit fabId when you belong to exactly one fab; name it when you belong to several (ADR-0114). Required scope: sse.rules.write")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{name}/publish", Publish)
            .WithName("PublishRule")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{name}/archive", Archive)
            .WithName("ArchiveRule")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Reads are a separate group: sse.rules.read, not the write scope
        // (spec 007 T090).
        RouteGroupBuilder reads = app.MapGroup("/rules")
            .RequireAuthorization(Scope.Sse.Rules.Read)
            .WithTags("Rules");

        reads.MapGet("/", List)
            .WithName("ListRules")
            .WithSummary("List rules in the fabs you are assigned to. Required scope: sse.rules.read")
            .Produces<IReadOnlyList<RuleDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        reads.MapGet("/{name}", GetOne)
            .WithName("GetRule")
            .Produces<RuleDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Dry-run is a POST because it carries a sample-event body, but it is
        // a read: nothing is persisted and no integration event is published,
        // so it sits behind the read scope.
        reads.MapPost("/{name}/dry-run", DryRun)
            .WithName("DryRunRule")
            .Produces<DryRunResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // The three filters are nullable because they are optional, and in a
    // project with NRT on that is the difference between an absent filter and
    // a rejected request: minimal APIs treat a non-nullable parameter with no
    // default as required, so a plain `GET /rules` never reached the handler.
    // It surfaced as a 500 rather than a 400 because the binding failure is an
    // exception, and UseExceptionHandler turns anything it does not recognise
    // into a generic problem response (#1298).
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
        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<IReadOnlyList<RuleDto>, ListRulesError> result = await handler.HandleAsync(
            new ListRulesQuery(fabs, state, triggerSource, triggerKind), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetOne(
        string name,
        HttpResponse response,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] GetRuleQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<RuleDto, GetRuleError> result = await handler.HandleAsync(
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
        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<DryRunResultDto, DryRunRuleError> result = await handler.HandleAsync(
            new DryRunRuleQuery(fabs, name, body.SampleEvent), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    /// <summary>
    /// Automation's binding of the shared decision table (ADR-0114) to its own
    /// <see cref="FabIdentifier"/>. The table itself lives in
    /// <see cref="FabResolution"/> so it can be tested without a realm — the
    /// multi-fab branch has no reachable user in the current deployment.
    /// </summary>
    private static async Task<Result<FabIdentifier, IResult>> ResolveWriteFabAsync(
        ClaimsPrincipal user, string fabId, IFabAuthorizationGuard fabGuard, CancellationToken cancellationToken)
    {
        Result<string, IResult> resolution = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "RULE_FAB_REQUIRED", cancellationToken);
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
                title: "RULE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

    private static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
        ClaimsPrincipal user, string fabId, IFabAuthorizationGuard fabGuard, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> resolved = await FabResolution.ResolveForReadAsync(
            user, fabId, fabGuard, cancellationToken);

        // Per entry, not all-or-nothing. A single group under /fabs/ that is
        // not a usable fab name — a sub-group, or a name outside Automation's
        // grammar — used to fail the whole read, hiding every rule in the fabs
        // the caller legitimately holds. One odd group should cost them that
        // group, not all of them.
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

    private static async Task<IResult> Create(
        [FromBody] CreateRuleRequest body,
        [AsParameters] CreateRuleServices services,
        HttpContext http,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(http).IsNotNull();

        IFabAuthorizationGuard fabGuard = services.FabGuard;
        CreateRuleCommandHandler handler = services.Handler;

        if (!IdempotencyHeaders.TryRead(http.Request, out Option<IdempotencyKey> key, out IResult? keyProblem))
        {
            return keyProblem;
        }

        Result<FabIdentifier, IResult> fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;

        RuleName name;
        RulePredicate predicate;
        RuleAction action;
        TriggerSource triggerSource;
        TriggerKind triggerKind;
        try
        {
            name = RuleName.From(body.Name);
            predicate = RulePredicate.From(body.Predicate);
            action = BuildAction(body);
            triggerSource = TriggerSource.From(body.TriggerSource);
            triggerKind = TriggerKind.From(body.TriggerKind);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "RULE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();

        // The location names the rule rather than its identifier, so the helper's
        // identifier argument is unused here — the route is stable across a replay
        // either way.
        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(supplied, CreateEndpoint, actingOperator.Value.ToString())),
                services.Idempotency,
                services.Clock),
            _ => $"/rules/{name.Value}",
            async token => (await handler.HandleAsync(
                    new CreateRuleCommand(fab, name, triggerSource, triggerKind, predicate, action, actingOperator),
                    token)).Match(
                onSuccess: id => Result<Guid, IResult>.Success(id.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084).
    /// </summary>
    private sealed record CreateRuleServices(
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] CreateRuleCommandHandler Handler,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> Publish(
        string name,
        HttpRequest request,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] PublishRuleCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Result<FabIdentifier, IResult> fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;

        RuleName parsed;
        try
        {
            parsed = RuleName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(title: "RULE_INVALID_INPUT", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        Result<RuleIdentifier, PublishRuleError> result = await handler.HandleAsync(new PublishRuleCommand(fab, parsed, expectedVersion), cancellationToken);

        return result.Match<IResult>(onSuccess: id => Results.Ok(id.Value), onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Archive(
        string name,
        HttpRequest request,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] ArchiveRuleCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Result<FabIdentifier, IResult> fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;

        RuleName parsed;
        try
        {
            parsed = RuleName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(title: "RULE_INVALID_INPUT", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        Result<RuleIdentifier, ArchiveRuleError> result = await handler.HandleAsync(new ArchiveRuleCommand(fab, parsed, expectedVersion), cancellationToken);

        return result.Match<IResult>(onSuccess: id => Results.Ok(id.Value), onFailure: error => error.ToProblem());
    }

    private static RuleAction BuildAction(CreateRuleRequest body)
    {
        return body.ActionType switch
        {
            SetVariableValue =>
                RuleAction.SetVariableValue.From(
                    body.VariableName ?? throw new ArgumentException("VariableName is required for SetVariableValue actions."),
                    body.ValueExpression ?? throw new ArgumentException("ValueExpression is required for SetVariableValue actions.")),

            HighlightOverlay =>
                RuleAction.HighlightOverlay.From(
                    body.OverlayIdentifier ?? throw new ArgumentException("OverlayIdentifier is required for HighlightOverlay actions."),
                    body.DurationMs ?? throw new ArgumentException("DurationMs is required for HighlightOverlay actions.")),

            _ => throw new ArgumentException($"Unknown ActionType '{body.ActionType}'. Expected: SetVariableValue | HighlightOverlay."),
        };
    }
}
