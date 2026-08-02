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

public static class DevicesEndpoints
{
    public static IEndpointRouteBuilder MapDevicesEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

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

        RouteGroupBuilder reads = app.MapGroup("/devices")
            .RequireAuthorization(Scope.Sse.Identity.DeviceClients.Read)
            .WithTags("IdentityDevices");

        reads.MapGet("/", List)
            .WithName("ListDevices")
            .WithSummary("List registered devices, optionally filtered by fab. Required scope: sse.identity.devices.read")
            .Produces<IReadOnlyList<RegisteredClientSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> List(
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] ListDevicesQueryHandler handler,
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
                    title: "DEVICE_INVALID_INPUT", detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await fabGuard.EnsureAccessAsync(user, parsed.Value, cancellationToken);
            fab = Option<FabIdentifier>.Some(parsed);
        }

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListDevicesQuery(fab), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterDeviceRequest body,
        [FromQuery] string fabId,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] RegisterDeviceCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

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

        await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<DeviceCredentialsDto, RegisterDeviceError> result = await handler.HandleAsync(
            new RegisterDeviceCommand(body.DeviceType, body.DeviceIdentifier, fab, actingOperator),
            cancellationToken);

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
            new DisableDeviceCommand(parsed), cancellationToken);

        return result.Match<IResult>(
            onSuccess: id => Results.Ok(id.Value),
            onFailure: error => error.ToProblem());
    }
}
