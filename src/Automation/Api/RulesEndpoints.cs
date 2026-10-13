using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.Automation.Api.Requests;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Api;

/// <summary>
/// Minimal-API endpoints for Automation rules (spec 007 / ADR-0070).
/// Writes require <see cref="Scope.Sse.Rules.Write"/>; reads (<c>GET /</c>,
/// <c>GET /{name}</c>, <c>POST /{name}/dry-run</c>) require
/// <see cref="Scope.Sse.Rules.Read"/>.
/// </summary>
public static partial class RulesEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string CreateEndpoint = "POST /rules";

    private const string SetVariableValue = "SetVariableValue";
    private const string HighlightOverlay = "HighlightOverlay";
    private const string SwitchWallScene = "SwitchWallScene";

    public static IEndpointRouteBuilder MapRulesEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        var group = app.MapGroup("/rules")
            .RequireAuthorization(Scope.Sse.Rules.Write)
            .WithTags("Rules");

        group.MapPost("/", Create)
            .WithName("CreateRule")
            .WithSummary("Author a rule. Omit fabId when you belong to exactly one fab; name it when you belong to several (ADR-0114). Required scope: sse.rules.write")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{name}/publish", Publish)
            .WithName("PublishRule")
            .WithSummary("Publish a rule. Requires If-Match with the rule's current version. Required scope: sse.rules.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{name}/archive", Archive)
            .WithName("ArchiveRule")
            .WithSummary("Archive a rule. Requires If-Match with the rule's current version. Required scope: sse.rules.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // Reads are a separate group: sse.rules.read, not the write scope
        // (spec 007 T090).
        var reads = app.MapGroup("/rules")
            .RequireAuthorization(Scope.Sse.Rules.Read)
            .WithTags("Rules");

        reads.MapGet("/", List)
            .WithName("ListRules")
            .WithSummary("List rules in the fabs you are assigned to. Required scope: sse.rules.read")
            .Produces<IReadOnlyList<RuleDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        reads.MapGet("/{name}", GetOne)
            .WithName("GetRule")
            .WithSummary("Read one rule by name, within the fabs you are assigned to. Required scope: sse.rules.read")
            .Produces<RuleDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Dry-run is a POST because it carries a sample-event body, but it is
        // a read: nothing is persisted and no integration event is published,
        // so it sits behind the read scope.
        reads.MapPost("/{name}/dry-run", DryRun)
            .WithName("DryRunRule")
            .WithSummary(
                "Evaluate a rule against a sample event. A POST because it carries a body, but a read: "
                + "nothing is persisted and no integration event is published. Required scope: sse.rules.read")
            .Produces<DryRunResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
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
        var resolution = await FabResolution.ResolveForWriteAsync(
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

        var fabGuard = services.FabGuard;
        var handler = services.Handler;

        if (!IdempotencyHeaders.TryRead(http.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        var fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

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

        var actingOperator = user.ToOperatorIdentifier();

        // The location names the rule rather than its identifier, so the helper's
        // identifier argument is unused here — the route is stable across a replay
        // either way.
        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(
                    supplied,
                    CreateEndpoint,
                    actingOperator.Value.ToString(),
                    Option<string>.Some(fab.Value),
                    IdempotencyFingerprint.Of(body))),
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
        var fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        RuleName parsed;
        try
        {
            parsed = RuleName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(title: "RULE_INVALID_INPUT", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var result = await handler.HandleAsync(new PublishRuleCommand(fab, parsed, expectedVersion), cancellationToken);

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
        var fabResolution =
            await ResolveWriteFabAsync(user, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        RuleName parsed;
        try
        {
            parsed = RuleName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(title: "RULE_INVALID_INPUT", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var result = await handler.HandleAsync(new ArchiveRuleCommand(fab, parsed, expectedVersion), cancellationToken);

        return result.Match<IResult>(onSuccess: id => Results.Ok(id.Value), onFailure: error => error.ToProblem());
    }

    private static RuleAction BuildAction(CreateRuleRequest body)
    {
        return body.ActionType switch
        {
            SetVariableValue => BuildSetVariableValue(body),
            HighlightOverlay => BuildHighlightOverlay(body),
            SwitchWallScene => BuildSwitchWallScene(body),
            _ => throw new ArgumentException($"Unknown ActionType '{body.ActionType}'. Expected: SetVariableValue | HighlightOverlay | SwitchWallScene."),
        };
    }

    private static RuleAction.SetVariableValue BuildSetVariableValue(CreateRuleRequest body)
    {
        return RuleAction.SetVariableValue.From(
            body.VariableName ?? throw new ArgumentException("VariableName is required for SetVariableValue actions."),
            body.ValueExpression ?? throw new ArgumentException("ValueExpression is required for SetVariableValue actions."));
    }

    private static RuleAction.HighlightOverlay BuildHighlightOverlay(CreateRuleRequest body)
    {
        return RuleAction.HighlightOverlay.From(
            body.OverlayIdentifier ?? throw new ArgumentException("OverlayIdentifier is required for HighlightOverlay actions."),
            body.DurationMs ?? throw new ArgumentException("DurationMs is required for HighlightOverlay actions."));
    }

    private static RuleAction.SwitchWallScene BuildSwitchWallScene(CreateRuleRequest body)
    {
        return RuleAction.SwitchWallScene.From(
            body.WallIdentifier ?? throw new ArgumentException("WallIdentifier is required for SwitchWallScene actions."),
            body.SceneTarget ?? throw new ArgumentException("SceneTarget is required for SwitchWallScene actions."),
            body.TargetLayoutIdentifier);
    }
}
