using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="AuthorizeMediaMtxApiCommand"/>.
/// One member: unlike the WHEP hook, there is no scope or fab distinction to
/// make here, only "the secret matched" or not.
/// </summary>
public abstract record AuthorizeMediaMtxApiError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record Unauthorized()
        : AuthorizeMediaMtxApiError(
            "MEDIAMTX_API_UNAUTHORIZED",
            "Basic-auth credential is missing or does not match the configured MediaMTX API secret.",
            HttpStatusCode.Unauthorized);
}

/// <summary>
/// Builds a <see cref="AuthorizeMediaMtxApiError"/> as the base rather than the
/// variant, mirroring <see cref="AuthorizeWhepFailures"/> (ADR-0047).
/// </summary>
public static class AuthorizeMediaMtxApiFailures
{
    public static AuthorizeMediaMtxApiError Unauthorized() =>
        new AuthorizeMediaMtxApiError.Unauthorized();
}
