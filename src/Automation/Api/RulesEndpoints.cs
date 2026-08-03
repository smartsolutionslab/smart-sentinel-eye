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
        [FromServices] ListRulesQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RuleDto>, ListRulesError> result = await handler.HandleAsync(
            new ListRulesQuery(state, triggerSource, triggerKind), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetOne(
        string name,
        HttpResponse response,
        [FromServices] GetRuleQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<RuleDto, GetRuleError> result = await handler.HandleAsync(
            new GetRuleQuery(name), cancellationToken);

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
        [FromServices] DryRunRuleQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        Result<DryRunResultDto, DryRunRuleError> result = await handler.HandleAsync(
            new DryRunRuleQuery(name, body.SampleEvent), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Create(
        [FromBody] CreateRuleRequest body,
        [FromQuery] string fabId,
        [FromServices] CreateRuleCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        // Explicit for now. Inferring it for a single-fab operator, and
        // refusing a multi-fab one who names none, is ADR-0114 and arrives
        // with the guard in spec 013 T032 — the resolution point is here.
        FabIdentifier fab;
        RuleName name;
        RulePredicate predicate;
        RuleAction action;
        try
        {
            fab = FabIdentifier.From(fabId);
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
        [FromServices] PublishRuleCommandHandler handler,
        CancellationToken cancellationToken)
    {
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

        Result<RuleIdentifier, PublishRuleError> result = await handler.HandleAsync(new PublishRuleCommand(parsed, expectedVersion), cancellationToken);

        return result.Match<IResult>(onSuccess: id => Results.Ok(id.Value), onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Archive(
        string name,
        HttpRequest request,
        [FromServices] ArchiveRuleCommandHandler handler,
        CancellationToken cancellationToken)
    {
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

        Result<RuleIdentifier, ArchiveRuleError> result = await handler.HandleAsync(new ArchiveRuleCommand(parsed, expectedVersion), cancellationToken);

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
