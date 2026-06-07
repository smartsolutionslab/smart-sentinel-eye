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

public static class KiosksEndpoints
{
    public static IEndpointRouteBuilder MapKiosksEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

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

        RouteGroupBuilder reads = app.MapGroup("/kiosks")
            .RequireAuthorization(Scope.Sse.Identity.KioskClients.Read)
            .WithTags("IdentityKiosks");

        reads.MapGet("/", List)
            .WithName("ListKiosks")
            .WithSummary("List enrolled kiosks, optionally filtered by fab. Required scope: sse.identity.kiosks.read")
            .Produces<IReadOnlyList<RegisteredClientSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> List(
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] ListKiosksQueryHandler handler,
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
                    title: "KIOSK_INVALID_INPUT", detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await fabGuard.EnsureAccessAsync(user, parsed.Value, cancellationToken);
            fab = Option<FabIdentifier>.Some(parsed);
        }

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListKiosksQuery(fab), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Enroll(
        [FromBody] EnrollKioskRequest body,
        [FromQuery] string fabId,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] EnrollKioskCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

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

        await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<KioskCredentialsDto, EnrollKioskError> result = await handler.HandleAsync(
            new EnrollKioskCommand(clientId, fab, actingOperator), cancellationToken);

        return result.Match<IResult>(
            onSuccess: dto => Results.Created($"/kiosks/{dto.ClientId}", dto),
            onFailure: error => error.ToProblem());
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
            new DisableKioskCommand(parsed), cancellationToken);

        return result.Match<IResult>(
            onSuccess: id => Results.Ok(id.Value),
            onFailure: error => error.ToProblem());
    }
}
