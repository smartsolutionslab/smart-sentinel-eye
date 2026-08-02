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

    /// <summary>
    /// The caller acted on a version of the device that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other contexts.
    /// </summary>
    public sealed record DeviceStale(string ClientId, int ExpectedVersion, int ActualVersion)
        : DisableDeviceError(
            "DEVICE_STALE",
            $"Device '{ClientId}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}
