using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

public abstract record EnrollKioskError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record KioskAlreadyEnrolled(string ClientId)
        : EnrollKioskError(
            "KIOSK_ALREADY_ENROLLED",
            $"A kiosk with clientId '{ClientId}' is already enrolled.",
            HttpStatusCode.Conflict);

    public sealed record KeycloakUnavailable(string Reason)
        : EnrollKioskError(
            "KEYCLOAK_UNAVAILABLE",
            $"Keycloak Admin API call failed: {Reason}",
            HttpStatusCode.BadGateway);
}

/// <summary>
/// Builds a <see cref="EnrollKioskError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class EnrollKioskFailures
{
    public static EnrollKioskError KioskAlreadyEnrolled(string clientId) =>
        new EnrollKioskError.KioskAlreadyEnrolled(clientId);

    public static EnrollKioskError KeycloakUnavailable(string reason) =>
        new EnrollKioskError.KeycloakUnavailable(reason);
}
