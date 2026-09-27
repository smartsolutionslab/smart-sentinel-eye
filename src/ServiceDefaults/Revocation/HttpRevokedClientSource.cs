using System.Net.Http.Json;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// Reads Identity's <c>GET /registered-clients/revoked</c> over HTTP
/// (spec 270, ADR-0160 §2). The default <see cref="IRevokedClientSource"/> for
/// every service except Identity itself, which replaces this registration
/// with an in-process reader (plan.md §3) rather than calling its own API.
///
/// <para>
/// Takes <see cref="IHttpClientFactory"/> rather than a typed <c>HttpClient</c>
/// so that this source can be a singleton — captured for the process lifetime
/// by the singleton <see cref="RevokedClientRefresher"/> — without pinning one
/// <see cref="HttpMessageHandler"/> forever (phase-6 review, S3). The same
/// reasoning as <c>ClientCredentialsTokenProvider</c>'s own doc comment and
/// <c>StreamFabAttributionService</c>'s per-scope resolution: a client
/// resolved once at construction never rotates with the factory.
/// </para>
/// </summary>
public sealed class HttpRevokedClientSource(IHttpClientFactory httpClientFactory) : IRevokedClientSource
{
    /// <summary>Named client this source fetches through, configured by <c>AddRevocationCheck</c>.</summary>
    public const string HttpClientName = "revocation-list-fetch";

    public async Task<IReadOnlyList<RevokedClientEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);

        RevokedClientsResponse response = await httpClient.GetFromJsonAsync<RevokedClientsResponse>(
            "/registered-clients/revoked", cancellationToken)
            ?? throw new InvalidOperationException(
                "Identity returned an empty response for GET /registered-clients/revoked.");

        return response.Clients;
    }
}
