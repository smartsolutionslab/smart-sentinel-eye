using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.Identity.Api.Requests;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
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
            .WithSummary("Rotate a webhook integration's bearer onto a Keycloak JWT. Requires If-Match with the version from GET /devices (send 0 on a first-time rotation, which has no client yet). Required scope: sse.webhooks.write")
            .Produces<WebhookClientCredentialsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
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

        // Required here as on every other mutating endpoint, rather than made
        // conditional on the client already existing: the caller cannot know
        // which branch it will take, and an optional header is the silent
        // opt-out ADR-0113 rejects. The handler ignores the value when there
        // is no client to have gone stale.
        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult precondition))
        {
            return precondition;
        }

        await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result = await handler.HandleAsync(
            new RotateWebhookClientCommand(name, fab, actingOperator, expectedVersion), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }
}
