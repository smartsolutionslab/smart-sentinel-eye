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
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Identity.Api;

public static class DevicesEndpoints
{
    public static IEndpointRouteBuilder MapDevicesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/devices")
            .RequireAuthorization(Scope.Sse.Identity.DeviceClients.Write)
            .WithTags("IdentityDevices");

        group.MapPost("/register", Register)
            .WithName("RegisterDevice")
            .WithSummary("Register a new PLC or inference device. Required scope: sse.identity.devices.write")
            .Produces<DeviceCredentialsDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{clientId}", Disable)
            .WithName("DisableDevice")
            .WithSummary("Disable a registered device. Required scope: sse.identity.devices.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterDeviceRequest body,
        [FromQuery] string fabId,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] RegisterDeviceCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        FabIdentifier fab;
        try
        {
            fab = FabIdentifier.From(fabId);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "DEVICE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken).ConfigureAwait(false);

        OperatorIdentifier op = OperatorClaim.From(user);
        Result<DeviceCredentialsDto, RegisterDeviceError> result = await handler.HandleAsync(
            new RegisterDeviceCommand(body.DeviceType, body.DeviceIdentifier, fab, op),
            cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: dto => Results.Created($"/devices/{dto.ClientId}", dto),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Disable(
        string clientId,
        [FromServices] DisableDeviceCommandHandler handler,
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
                title: "DEVICE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<RegisteredClientIdentifier, DisableDeviceError> result = await handler.HandleAsync(
            new DisableDeviceCommand(parsed), cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: id => Results.Ok(id.Value),
            onFailure: error => error.ToProblem());
    }
}
