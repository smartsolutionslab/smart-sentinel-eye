using System.Net.Http.Headers;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authentication;

/// <summary>
/// Sets the <c>Authorization</c> header on each outbound request, from whatever
/// the deriving handler decides the caller is.
///
/// <para>
/// Three clients had written the header onto
/// <c>HttpClient.DefaultRequestHeaders</c> instead — Identity's Keycloak admin
/// client, StreamDistribution's camera lookup, and LayoutComposition's fab
/// guard. That is client-wide state being used to carry per-request data, and it
/// was correct only because each typed client happens to be registered
/// transient. The correctness lived in a registration one file away, not in the
/// code doing the writing, and the failure it invites is an authorisation that
/// is too permissive — the kind that produces no error at all.
/// </para>
///
/// <para>
/// A handler also sits <i>inside</i> the standard resilience pipeline, because
/// ServiceDefaults adds that through <c>ConfigureHttpClientDefaults</c> and
/// defaults are applied before per-client handlers. So a retry re-enters here
/// and re-reads the credential, which is what should happen when the reason for
/// retrying was a token that expired in flight.
/// </para>
/// </summary>
public abstract class AuthorizingHandler : DelegatingHandler
{
    /// <summary>
    /// The credential for this request, or <c>null</c> to send none. Null is a
    /// real answer, not a failure: LayoutComposition's guard forwards a caller's
    /// own token and must let an unauthenticated call go out and be refused,
    /// rather than substituting something more privileged.
    /// </summary>
    protected abstract Task<AuthenticationHeaderValue?> AuthorizationAsync(CancellationToken cancellationToken);

    protected sealed override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Ensure.That(request).IsNotNull();

        request.Headers.Authorization = await AuthorizationAsync(cancellationToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
