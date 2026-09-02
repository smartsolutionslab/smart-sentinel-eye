using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.Identity.Api.Requests;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Api;

/// <summary>
/// Endpoint for the hard-cut webhook migration (spec 008 US5 /
/// FR-014). Admins call this to lift a spec-006 webhook integration
/// onto a Keycloak service-account client; subsequent calls roll
/// the secret.
/// </summary>
public static class WebhookRotationEndpoints
{
    public static IEndpointRouteBuilder MapWebhookRotationEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder group = app.MapGroup("/webhook-integrations")
            .RequireAuthorization(Scope.Sse.Webhooks.Write)
            .WithTags("IdentityWebhookRotation");

        group.MapPost("/{name}/rotate", Rotate)
            .WithName("RotateWebhookClient")
            .WithSummary("Rotate a webhook integration's bearer onto a Keycloak JWT. Send If-Match with the version from GET /webhook-integrations to rotate an existing client, or If-None-Match: * to create one. Required scope: sse.webhooks.write")
            .Produces<WebhookClientCredentialsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // Same scope as the rotation rather than a new sse.webhooks.read: this
        // list exists only to supply the If-Match the rotation demands, so
        // anyone who may rotate must be able to read it. A separate read scope
        // would let a principal hold the write scope its own description
        // promises and still be unable to rotate anything.
        group.MapGet("/", List)
            .WithName("ListWebhookClients")
            .WithSummary("List webhook service-account clients and the version each must be rotated at. Required scope: sse.webhooks.write")
            .Produces<IReadOnlyList<RegisteredClientSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> List(
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] ListWebhookClientsQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string? fabId = null)
    {
        Option<FabIdentifier> fab;
        if (string.IsNullOrWhiteSpace(fabId))
        {
            fab = Option<FabIdentifier>.None;
        }
        else
        {
            FabIdentifier parsed;
            try
            {
                parsed = FabIdentifier.From(fabId);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    title: "WEBHOOK_INVALID_INPUT", detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await fabGuard.EnsureAccessAsync(user, parsed.Value, cancellationToken);
            fab = Option<FabIdentifier>.Some(parsed);
        }

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListWebhookClientsQuery(fab), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Rotate(
        string name,
        [FromBody] RotateWebhookClientRequest body,
        HttpRequest request,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] RotateWebhookClientCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        FabIdentifier fab;
        try
        {
            fab = FabIdentifier.From(body.FabId);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "WEBHOOK_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The guard runs before the precondition is parsed, per
        // IFabAuthorizationGuard's contract that it is called right after model
        // binding. Reading If-Match first would answer a caller who may not
        // touch this fab at all with 428 and an invitation to retry, instead of
        // the 403 they have earned.
        await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);

        // This endpoint upserts, so the caller states which operation it means:
        // If-None-Match: * to create the client, If-Match: "N" to rotate the
        // one at version N. See ConcurrencyHeaders for why a lone If-Match
        // cannot carry both.
        if (!ConcurrencyHeaders.TryReadUpsertPrecondition(request, out Option<int> expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result = await handler.HandleAsync(
            new RotateWebhookClientCommand(name, fab, actingOperator, expectedVersion), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }
}
