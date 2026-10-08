using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.Identity.Infrastructure.WebhookIntegrations;

/// <summary>
/// Forwards the incoming request's <c>Authorization</c> header to
/// EventIngestion (spec 318, #2628) — Identity's copy of
/// <c>LayoutComposition.Infrastructure.Cameras.CallerTokenForwardingHandler</c>
/// (spec 017 plan §III), verbatim in behaviour.
///
/// <para>
/// The credential is the operator's own, not a service account: the status
/// lookup sees exactly what the rotating caller can already see and nothing
/// more. Absent a header the call goes out unauthenticated and EventIngestion
/// answers 401 — a refused rotation rather than an accepted one, so the
/// failure direction is closed. Returning null here is what preserves that;
/// substituting anything else would widen the exception silently.
/// </para>
///
/// <para>
/// <see cref="IHttpContextAccessor"/> is safe to hold even though the handler
/// outlives any one request: it is a singleton over an <c>AsyncLocal</c>, so it
/// reports the context of whichever request is executing when it is read, not
/// the one that was current when the handler was built.
/// </para>
/// </summary>
public sealed class CallerTokenForwardingHandler(IHttpContextAccessor httpContextAccessor) : AuthorizingHandler
{
    protected override Task<AuthenticationHeaderValue?> AuthorizationAsync(CancellationToken cancellationToken)
    {
        string? incoming = httpContextAccessor.HttpContext?.Request.Headers.Authorization;

        return Task.FromResult(
            AuthenticationHeaderValue.TryParse(incoming, out AuthenticationHeaderValue? parsed) ? parsed : null);
    }
}
