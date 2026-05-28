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
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/webhook-integrations")
            .RequireAuthorization(AuthenticationDefaults.AdminPolicy)
            .WithTags("EventIngestion");

        group.MapPost("/", Register)
            .Produces<RegisteredWebhookResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", List)
            .Produces<IReadOnlyList<WebhookIntegrationDto>>(StatusCodes.Status200OK);

        group.MapDelete("/{name}", Revoke)
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterWebhookIntegrationRequest body,
        [FromServices] RegisterWebhookIntegrationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

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

        Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError> result =
            await handler.HandleAsync(
                new RegisterWebhookIntegrationCommand(name, defaultKind),
                cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: r => Results.Created(
                $"/webhook-integrations/{name.Value}",
                new RegisteredWebhookResponse(r.Identifier.Value, name.Value, r.PlainToken)),
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    private static async Task<IResult> List(
        [FromQuery] bool? includeRevoked,
        [FromServices] ListWebhookIntegrationsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError> result =
            await handler.HandleAsync(
                new ListWebhookIntegrationsQuery(includeRevoked ?? false),
                cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    private static async Task<IResult> Revoke(
        string name,
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

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> result =
            await handler.HandleAsync(
                new RevokeWebhookIntegrationCommand(parsed),
                cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Ok(identifier.Value),
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    public sealed record RegisteredWebhookResponse(Guid Identifier, string Name, string Token);
}
