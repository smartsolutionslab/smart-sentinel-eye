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
