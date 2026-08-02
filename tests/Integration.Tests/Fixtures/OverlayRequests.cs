using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Sends a mutating overlay request with the <c>If-Match</c> precondition the
/// API requires (ADR-0113 Layer 1). Mirrors <see cref="LayoutRequests"/>;
/// ADR-0104 keeps the two revisioned contexts in step, including their test
/// scaffolding.
///
/// <para>
/// Reads the chain version first, so a test that is not about staleness does
/// not track versions by hand. **Do not use inside a measured window** — the
/// extra round trip is charged to the budget. Those call sites read the
/// version before starting the clock, or send a version they already know.
/// </para>
/// </summary>
internal static class OverlayRequests
{
    internal static async Task<int> VersionAsync(HttpClient overlays, Guid overlayIdentifier)
    {
        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetched.EnsureSuccessStatusCode();
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("version").GetInt32();
    }

    internal static HttpRequestMessage Conditional(HttpMethod method, Guid overlayIdentifier, string relativeUrl, int version)
    {
        HttpRequestMessage request = new(method, $"/overlays/{overlayIdentifier}/{relativeUrl}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        return request;
    }

    internal static async Task<HttpResponseMessage> PostAsync(
        HttpClient overlays, Guid overlayIdentifier, string relativeUrl)
    {
        int version = await VersionAsync(overlays, overlayIdentifier);

        return await overlays.SendAsync(Conditional(HttpMethod.Post, overlayIdentifier, relativeUrl, version));
    }

    internal static async Task<HttpResponseMessage> PatchAsync(
        HttpClient overlays, Guid overlayIdentifier, string relativeUrl, object body)
    {
        int version = await VersionAsync(overlays, overlayIdentifier);
        HttpRequestMessage request = Conditional(HttpMethod.Patch, overlayIdentifier, relativeUrl, version);
        request.Content = JsonContent.Create(body);

        return await overlays.SendAsync(request);
    }
}
