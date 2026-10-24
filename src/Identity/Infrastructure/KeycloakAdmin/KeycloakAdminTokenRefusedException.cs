using System.Net;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// Keycloak's token endpoint refused the admin client's credential (400/401). Distinct from
/// the same statuses coming back from the admin API after a successful mint, which are a
/// different fault that must not be reported as a credential problem (spec 440 FR-004b).
/// </summary>
public sealed class KeycloakAdminTokenRefusedException(
    string message,
    HttpStatusCode? statusCode,
    Exception? innerException = null)
    : HttpRequestException(message, innerException, statusCode);
