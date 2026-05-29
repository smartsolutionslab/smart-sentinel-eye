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
