using System.Net;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

public sealed record DisableDeviceCommand(ClientId ClientId)
    : ICommand<Result<RegisteredClientIdentifier, DisableDeviceError>>;

public abstract record DisableDeviceError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record DeviceNotFound(string ClientId)
        : DisableDeviceError(
            "DEVICE_NOT_FOUND",
            $"No registered device with clientId '{ClientId}' exists.",
            HttpStatusCode.NotFound);

    public sealed record KeycloakUnavailable(string Reason)
        : DisableDeviceError(
            "KEYCLOAK_UNAVAILABLE",
            $"Keycloak Admin API call failed: {Reason}",
            HttpStatusCode.BadGateway);
}

/// <summary>
/// Builds a <see cref="DisableDeviceError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class DisableDeviceFailures
{
    public static DisableDeviceError DeviceNotFound(string clientId) =>
        new DisableDeviceError.DeviceNotFound(clientId);

    public static DisableDeviceError KeycloakUnavailable(string reason) =>
        new DisableDeviceError.KeycloakUnavailable(reason);
}
