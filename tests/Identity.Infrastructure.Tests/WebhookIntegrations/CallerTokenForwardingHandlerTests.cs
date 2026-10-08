using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using SmartSentinelEye.Identity.Infrastructure.WebhookIntegrations;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.WebhookIntegrations;

/// <summary>
/// Spec 318 (#2628), plan §3 — Identity's copy of LayoutComposition's
/// <c>CallerTokenForwardingHandler</c> (spec 017 plan §III), verbatim in
/// behaviour. No production test exists for the original to mirror (plan
/// §8.2 notes this explicitly), so these are new.
/// </summary>
public class CallerTokenForwardingHandlerTests
{
    [Fact]
    public async Task The_incoming_requests_authorization_header_is_forwarded_unchanged()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["Authorization"] = "Bearer caller-token";
        HttpContextAccessor accessor = new() { HttpContext = context };

        RecordingHandler downstream = new();
        using HttpClient client = Compose(new CallerTokenForwardingHandler(accessor), downstream);

        await client.GetAsync("https://event-ingestion.test/webhook-integrations", CancellationToken.None);

        downstream.LastAuthorization?.Scheme.ShouldBe("Bearer");
        downstream.LastAuthorization?.Parameter.ShouldBe("caller-token");
    }

    /// <summary>
    /// No request in flight (there should always be one — a rotation only
    /// ever runs inside an HTTP request, plan §3 — but the handler must not
    /// substitute a privileged credential if that assumption is ever wrong).
    /// Closed by construction: EventIngestion answers 401 to an
    /// unauthenticated call, which the adapter maps to <c>Unverifiable</c>.
    /// </summary>
    [Fact]
    public async Task No_HttpContext_sends_no_authorization_header_at_all()
    {
        HttpContextAccessor accessor = new();

        RecordingHandler downstream = new();
        using HttpClient client = Compose(new CallerTokenForwardingHandler(accessor), downstream);

        await client.GetAsync("https://event-ingestion.test/webhook-integrations", CancellationToken.None);

        downstream.SawAuthorizationHeader.ShouldBeFalse(
            "a missing caller token must send the request unauthenticated, not substitute anything "
            + "more privileged (spec 318 plan §3)");
    }

    [Fact]
    public async Task No_authorization_header_on_the_incoming_request_sends_none_outbound_either()
    {
        DefaultHttpContext context = new();
        HttpContextAccessor accessor = new() { HttpContext = context };

        RecordingHandler downstream = new();
        using HttpClient client = Compose(new CallerTokenForwardingHandler(accessor), downstream);

        await client.GetAsync("https://event-ingestion.test/webhook-integrations", CancellationToken.None);

        downstream.SawAuthorizationHeader.ShouldBeFalse();
    }

    private static HttpClient Compose(CallerTokenForwardingHandler forwarding, RecordingHandler downstream)
    {
        forwarding.InnerHandler = downstream;
        return new HttpClient(forwarding);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        public bool SawAuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization;
            SawAuthorizationHeader = request.Headers.Contains("Authorization");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
