using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// Admin-only endpoints for managing webhook integrations
/// (spec 006 FR-023 / FR-024 / FR-025).
/// </summary>
public static class WebhookIntegrationsEndpoints
{
    public static IEndpointRouteBuilder MapWebhookIntegrationsEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder group = app.MapGroup("/webhook-integrations")
            .RequireAuthorization(Scope.Sse.Webhooks.Write)
            .WithTags("EventIngestion");

        // 403 became reachable when the registry became fab-owned (#1545): the
        // caller may name a fab they do not hold, or hold none at all. 400 now
        // also covers a multi-fab admin registering without naming one.
        group.MapPost("/", Register)
            .WithSummary(
                "Register a webhook integration for the resolved fab. Omit fabId when you belong "
                + "to exactly one; name it when you belong to several (ADR-0114). The integration's "
                + "fab decides which plant its deliveries may name.")
            .Produces<RegisteredWebhookResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", List)
            .WithSummary("List the webhook integrations of the fabs you hold.")
            .Produces<IReadOnlyList<WebhookIntegrationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{name}", Revoke)
            .WithSummary("Revoke a webhook integration. Requires If-Match with the version from GET /webhook-integrations.")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterWebhookIntegrationRequest body,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromServices] RegisterWebhookIntegrationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        WebhookIntegrationName name;
        Kind defaultKind;
        try
        {
            name = WebhookIntegrationName.From(body.Name);
            defaultKind = Kind.From(body.DefaultKind);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "WEBHOOK_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The integration's plant is the registering operator's, resolved the
        // same way the manual write resolves an event's (#1545). It decides
        // which plant the integration's deliveries may name, so it is a write
        // resolution rather than a read: a multi-fab admin must choose.
        (FabIdentifier? fab, IResult? fabProblem) =
            await EventIngestionFabResolution.ResolveWriteFabAsync(
                user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fab is null)
        {
            return fabProblem!;
        }

        Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError> result =
            await handler.HandleAsync(
                new RegisterWebhookIntegrationCommand(name, fab, defaultKind),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: registration => Results.Created(
                $"/webhook-integrations/{name.Value}",
                new RegisteredWebhookResponse(registration.Identifier.Value, name.Value, registration.PlainToken)),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> List(
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromQuery] bool? includeRevoked,
        [FromServices] ListWebhookIntegrationsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await EventIngestionFabResolution.ResolveReadFabsAsync(
                user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

        Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError> result =
            await handler.HandleAsync(
                new ListWebhookIntegrationsQuery(fabs, includeRevoked ?? false),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Revoke(
        string name,
        HttpRequest request,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromServices] RevokeWebhookIntegrationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        WebhookIntegrationName parsed;
        try
        {
            parsed = WebhookIntegrationName.From(name);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "WEBHOOK_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await EventIngestionFabResolution.ResolveReadFabsAsync(
                user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> result =
            await handler.HandleAsync(
                new RevokeWebhookIntegrationCommand(fabs, parsed, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Ok(identifier.Value),
            onFailure: error => error.ToProblem());
    }

    public sealed record RegisteredWebhookResponse(Guid Identifier, string Name, string Token);
}
