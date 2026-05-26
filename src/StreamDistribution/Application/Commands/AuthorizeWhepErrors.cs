using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="AuthorizeWhepCommand"/>.
/// MediaMTX uses the HTTP status to allow or reject the WHEP handshake.
/// </summary>
public abstract record AuthorizeWhepError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record Unauthorized()
        : AuthorizeWhepError(
            "WHEP_UNAUTHORIZED",
            "Bearer token is missing, malformed, or expired.",
            HttpStatusCode.Unauthorized);

    public sealed record Forbidden()
        : AuthorizeWhepError(
            "WHEP_FORBIDDEN",
            "Bearer token does not grant the sse.management scope.",
            HttpStatusCode.Forbidden);

    public sealed record StreamUnavailable()
        : AuthorizeWhepError(
            "WHEP_STREAM_UNAVAILABLE",
            "The requested stream is offline; cannot open a WHEP session.",
            HttpStatusCode.Forbidden);
}
