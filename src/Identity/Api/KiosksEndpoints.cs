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

public static class KiosksEndpoints
{
    public static IEndpointRouteBuilder MapKiosksEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/kiosks")
            .RequireAuthorization(Scope.Sse.Identity.KioskClients.Write)
            .WithTags("IdentityKiosks");

        group.MapPost("/enroll", Enroll)
            .WithName("EnrollKiosk")
            .WithSummary("Enroll a new kiosk in the fab. Required scope: sse.identity.kiosks.write")
            .Produces<KioskCredentialsDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{clientId}", Disable)
            .WithName("DisableKiosk")
            .WithSummary("Disable an enrolled kiosk. Required scope: sse.identity.kiosks.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> Enroll(
        [FromBody] EnrollKioskRequest body,
        [FromQuery] string fabId,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] EnrollKioskCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        ClientId clientId;
        FabIdentifier fab;
        try
        {
            clientId = ClientId.From(body.ClientId);
            fab = FabIdentifier.From(fabId);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "KIOSK_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken).ConfigureAwait(false);

        OperatorIdentifier op = OperatorClaim.From(user);
        Result<KioskCredentialsDto, EnrollKioskError> result = await handler.HandleAsync(
            new EnrollKioskCommand(clientId, fab, op), cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: dto => Results.Created($"/kiosks/{dto.ClientId}", dto),
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    private static async Task<IResult> Disable(
        string clientId,
        [FromServices] DisableKioskCommandHandler handler,
        CancellationToken cancellationToken)
    {
        ClientId parsed;
        try
        {
            parsed = ClientId.From(clientId);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "KIOSK_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<RegisteredClientIdentifier, DisableKioskError> result = await handler.HandleAsync(
            new DisableKioskCommand(parsed), cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: id => Results.Ok(id.Value),
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }
}
