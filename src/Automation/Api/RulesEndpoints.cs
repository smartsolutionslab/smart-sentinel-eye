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
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Api;

/// <summary>
/// Minimal-API endpoints for Automation rules (spec 007 / ADR-0070).
/// All writes require <see cref="AuthenticationDefaults.AdminPolicy"/>;
/// read endpoints land in PR F (polish) along with the dry-run path.
/// </summary>
public static class RulesEndpoints
{
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
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{name}/publish", Publish)
            .WithName("PublishRule")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{name}/archive", Archive)
            .WithName("ArchiveRule")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Reads are a separate group: sse.rules.read, not the write scope
        // (spec 007 T090).
        RouteGroupBuilder reads = app.MapGroup("/rules")
            .RequireAuthorization(Scope.Sse.Rules.Read)
            .WithTags("Rules");

        reads.MapGet("/", List)
            .WithName("ListRules")
            .Produces<IReadOnlyList<RuleDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        reads.MapGet("/{name}", GetOne)
            .WithName("GetRule")
            .Produces<RuleDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Dry-run is a POST because it carries a sample-event body, but it is
        // a read: nothing is persisted and no integration event is published,
        // so it sits behind the read scope.
        reads.MapPost("/{name}/dry-run", DryRun)
            .WithName("DryRunRule")
            .Produces<DryRunResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> List(
        [FromQuery] string state,
        [FromQuery] string triggerSource,
        [FromQuery] string triggerKind,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] ListRulesQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

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
        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

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
        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

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
    private static async Task<(FabIdentifier? Fab, IResult? Problem)> ResolveWriteFabAsync(
        ClaimsPrincipal user, string fabId, IFabAuthorizationGuard fabGuard, CancellationToken cancellationToken)
    {
        (string resolved, IResult problem) = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "RULE_FAB_REQUIRED", cancellationToken);
        if (problem is not null)
        {
            return (null, problem);
        }

        try
        {
            return (FabIdentifier.From(resolved), null);
        }
        catch (ArgumentException ex)
        {
            return (null, Results.Problem(
                title: "RULE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

    private static async Task<(IReadOnlyList<FabIdentifier>? Fabs, IResult? Problem)> ResolveReadFabsAsync(
        ClaimsPrincipal user, string fabId, IFabAuthorizationGuard fabGuard, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> resolved = await FabResolution.ResolveForReadAsync(
            user, fabId, fabGuard, cancellationToken);

        try
        {
            return ([.. resolved.Select(FabIdentifier.From)], null);
        }
        catch (ArgumentException ex)
        {
            return (null, Results.Problem(
                title: "RULE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

    private static async Task<IResult> Create(
        [FromBody] CreateRuleRequest body,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] CreateRuleCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(body).IsNotNull();

        (FabIdentifier? fab, IResult? fabProblem) =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fab is null)
        {
            return fabProblem!;
        }

        RuleName name;
        RulePredicate predicate;
        RuleAction action;
        try
        {
            name = RuleName.From(body.Name);
            predicate = RulePredicate.From(body.Predicate);
            action = BuildAction(body);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "RULE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<RuleIdentifier, CreateRuleError> result = await handler.HandleAsync(
            new CreateRuleCommand(fab, name, body.TriggerSource, body.TriggerKind, predicate, action, actingOperator),
            cancellationToken);

        return result.Match<IResult>(
            onSuccess: id => Results.Created($"/rules/{name.Value}", id.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Publish(
        string name,
        HttpRequest request,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] PublishRuleCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        (FabIdentifier? fab, IResult? fabProblem) =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fab is null)
        {
            return fabProblem!;
        }

        RuleName parsed;
        try
        {
            parsed = RuleName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(title: "RULE_INVALID_INPUT", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
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
        (FabIdentifier? fab, IResult? fabProblem) =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fab is null)
        {
            return fabProblem!;
        }

        RuleName parsed;
        try
        {
            parsed = RuleName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(title: "RULE_INVALID_INPUT", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
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
