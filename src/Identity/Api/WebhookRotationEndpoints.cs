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
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/webhook-integrations")
            .RequireAuthorization(Scope.Sse.Webhooks.Write)
            .WithTags("IdentityWebhookRotation");

        group.MapPost("/{name}/rotate", Rotate)
            .WithName("RotateWebhookClient")
            .WithSummary("Rotate a webhook integration's bearer onto a Keycloak JWT. Required scope: sse.webhooks.write")
            .Produces<WebhookClientCredentialsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
    }

    private static async Task<IResult> Rotate(
        string name,
        [FromBody] RotateWebhookClientRequest body,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] RotateWebhookClientCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

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

        await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken).ConfigureAwait(false);

        OperatorIdentifier op = OperatorClaim.From(user);
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result = await handler.HandleAsync(
            new RotateWebhookClientCommand(name, fab, op), cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }
}
