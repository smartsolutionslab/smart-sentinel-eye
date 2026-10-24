using System.Net;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// COMPILE STUB (spec 440 T163/T164, phase 4a): type and constructor only, so the tests
/// that name it compile. Nothing raises it yet; T180 wires the mint.
/// </summary>
public sealed class KeycloakAdminTokenRefusedException(
    string message,
    HttpStatusCode? statusCode,
    Exception? innerException = null)
    : HttpRequestException(message, innerException, statusCode);
