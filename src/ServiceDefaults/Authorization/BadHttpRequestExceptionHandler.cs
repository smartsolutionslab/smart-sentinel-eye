using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Honours the status code a <see cref="BadHttpRequestException"/> already
/// carries, instead of letting it fall through to the generic 500.
///
/// <para>
/// Minimal APIs raise this for a request the caller got wrong — a missing
/// required query parameter, a body that will not bind, one that exceeds the
/// size limit — with the right status already on the exception. But it is
/// still an exception, so <c>UseExceptionHandler</c> caught it and wrote
/// "An error occurred while processing your request", status 500. The caller
/// was told the server had failed and invited to retry something that could
/// never succeed (#1312).
/// </para>
///
/// <para>
/// Registered before the typed handlers in
/// <c>ServiceDefaults.AddBearerAuthentication</c>. It reports the exception's
/// own status rather than assuming 400, because this type carries others —
/// 413 when a body exceeds <c>MaxRequestBodySize</c>, 415 on an unsupported
/// content type. No body-size limit is configured today (an oversized payload
/// is refused by the domain as <c>EVENT_INVALID_INPUT</c>, a 400), so the 413
/// path is unreachable until #597 wires one up; hard-coding 400 here would
/// quietly break it on the day someone does.
/// </para>
/// </summary>
public sealed class BadHttpRequestExceptionHandler : IExceptionHandler
{
    public const string ErrorCode = "REQUEST_INVALID";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        Ensure.That(httpContext).IsNotNull();

        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;
        ProblemDetails problem = new()
        {
            Title = ErrorCode,
            // The framework's message names the parameter it could not bind
            // ("Required parameter \"string fabId\" was not provided from
            // query string."), which is the one thing that makes this
            // actionable. It describes the request, not the server.
            Detail = badRequest.Message,
            Status = badRequest.StatusCode,
        };
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken: cancellationToken);

        return true;
    }
}
