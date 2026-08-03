using System.Net;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

public sealed record DisableKioskCommand(ClientId ClientId)
    : ICommand<Result<RegisteredClientIdentifier, DisableKioskError>>;

public abstract record DisableKioskError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record KioskNotFound(string ClientId)
        : DisableKioskError(
            "KIOSK_NOT_FOUND",
            $"No enrolled kiosk with clientId '{ClientId}' exists.",
            HttpStatusCode.NotFound);

    public sealed record KeycloakUnavailable(string Reason)
        : DisableKioskError(
            "KEYCLOAK_UNAVAILABLE",
            $"Keycloak Admin API call failed: {Reason}",
            HttpStatusCode.BadGateway);
}

/// <summary>
/// Builds a <see cref="DisableKioskError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class DisableKioskFailures
{
    public static DisableKioskError KioskNotFound(string clientId) =>
        new DisableKioskError.KioskNotFound(clientId);

    public static DisableKioskError KeycloakUnavailable(string reason) =>
        new DisableKioskError.KeycloakUnavailable(reason);
}
