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
            "Bearer token does not grant the sse.streams.read scope.",
            HttpStatusCode.Forbidden);

    public sealed record StreamUnavailable()
        : AuthorizeWhepError(
            "WHEP_STREAM_UNAVAILABLE",
            "The requested stream is offline; cannot open a WHEP session.",
            HttpStatusCode.Forbidden);

    /// <summary>
    /// The hook named an operation this product never grants through it.
    /// <c>403</c> and never <c>401</c>: upstream documents <c>401</c> as how an
    /// auth server asks a client to come back with credentials, and no
    /// credential makes a publish acceptable here — so the refusal has to be
    /// terminal rather than an invitation to retry.
    /// </summary>
    public sealed record ActionNotPermitted()
        : AuthorizeWhepError(
            "WHEP_ACTION_NOT_PERMITTED",
            "The requested action is not permitted through this hook.",
            HttpStatusCode.Forbidden);

    /// <summary>
    /// The hook named no action at all, or one this product does not model.
    /// Refused rather than assumed to be a read — the same <c>403</c>, for the
    /// same reason.
    /// </summary>
    public sealed record ActionUnknown()
        : AuthorizeWhepError(
            "WHEP_ACTION_UNKNOWN",
            "The request named no recognised action.",
            HttpStatusCode.Forbidden);
}

/// <summary>
/// Builds a <see cref="AuthorizeWhepError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class AuthorizeWhepFailures
{
    public static AuthorizeWhepError Unauthorized() =>
        new AuthorizeWhepError.Unauthorized();

    public static AuthorizeWhepError Forbidden() =>
        new AuthorizeWhepError.Forbidden();

    public static AuthorizeWhepError StreamUnavailable() =>
        new AuthorizeWhepError.StreamUnavailable();

    public static AuthorizeWhepError ActionNotPermitted() =>
        new AuthorizeWhepError.ActionNotPermitted();

    public static AuthorizeWhepError ActionUnknown() =>
        new AuthorizeWhepError.ActionUnknown();
}
