using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.Automation.Api.Requests;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.ServiceDefaults;
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
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/rules")
            .RequireAuthorization(AuthenticationDefaults.AdminPolicy)
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

        return app;
    }

    private static async Task<IResult> Create(
        [FromBody] CreateRuleRequest body,
        [FromServices] CreateRuleCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

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

        OperatorIdentifier op = OperatorFromClaims(user);
        Result<RuleIdentifier, CreateRuleError> result = await handler.HandleAsync(
            new CreateRuleCommand(name, body.TriggerSource, body.TriggerKind, predicate, action, op),
            cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: id => Results.Created($"/rules/{name.Value}", id.Value),
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    private static async Task<IResult> Publish(
        string name,
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
            return Results.Problem(
                title: "RULE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<RuleIdentifier, PublishRuleError> result = await handler.HandleAsync(
            new PublishRuleCommand(parsed), cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: id => Results.Ok(id.Value),
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    private static async Task<IResult> Archive(
        string name,
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
            return Results.Problem(
                title: "RULE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<RuleIdentifier, ArchiveRuleError> result = await handler.HandleAsync(
            new ArchiveRuleCommand(parsed), cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: id => Results.Ok(id.Value),
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    private static RuleAction BuildAction(CreateRuleRequest body)
    {
        return body.ActionType switch
        {
            SetVariableValue =>
                RuleAction.SetVariableValue.From(
                    body.VariableName ?? throw new ArgumentException(
                        "VariableName is required for SetVariableValue actions."),
                    body.ValueExpression ?? throw new ArgumentException(
                        "ValueExpression is required for SetVariableValue actions.")),

            HighlightOverlay =>
                RuleAction.HighlightOverlay.From(
                    body.OverlayIdentifier ?? throw new ArgumentException(
                        "OverlayIdentifier is required for HighlightOverlay actions."),
                    body.DurationMs ?? throw new ArgumentException(
                        "DurationMs is required for HighlightOverlay actions.")),

            _ => throw new ArgumentException(
                $"Unknown ActionType '{body.ActionType}'. Expected: SetVariableValue | HighlightOverlay."),
        };
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
