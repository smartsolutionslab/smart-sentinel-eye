using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

public abstract record RegisterDeviceError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record DeviceAlreadyRegistered(string ClientId)
        : RegisterDeviceError(
            "DEVICE_ALREADY_REGISTERED",
            $"A device with clientId '{ClientId}' is already registered.",
            HttpStatusCode.Conflict);

    public sealed record InvalidDeviceType(string DeviceType)
        : RegisterDeviceError(
            "DEVICE_INVALID_TYPE",
            $"deviceType '{DeviceType}' is not allowed; expected: plc | inference.",
            HttpStatusCode.BadRequest);

    public sealed record InvalidDeviceIdentifier(string Reason)
        : RegisterDeviceError(
            "DEVICE_INVALID_IDENTIFIER",
            $"deviceIdentifier rejected: {Reason}",
            HttpStatusCode.BadRequest);

    public sealed record KeycloakUnavailable(string Reason)
        : RegisterDeviceError(
            "KEYCLOAK_UNAVAILABLE",
            $"Keycloak Admin API call failed: {Reason}",
            HttpStatusCode.BadGateway);
}

/// <summary>
/// Builds a <see cref="RegisterDeviceError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RegisterDeviceFailures
{
    public static RegisterDeviceError DeviceAlreadyRegistered(string clientId) =>
        new RegisterDeviceError.DeviceAlreadyRegistered(clientId);

    public static RegisterDeviceError InvalidDeviceType(string deviceType) =>
        new RegisterDeviceError.InvalidDeviceType(deviceType);

    public static RegisterDeviceError InvalidDeviceIdentifier(string reason) =>
        new RegisterDeviceError.InvalidDeviceIdentifier(reason);

    public static RegisterDeviceError KeycloakUnavailable(string reason) =>
        new RegisterDeviceError.KeycloakUnavailable(reason);
}
