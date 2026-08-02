using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Sends a mutating layout request with the <c>If-Match</c> precondition the
/// API now requires (ADR-0113 Layer 1). Reads the chain's current version
/// first, so a test that is not specifically about staleness does not have to
/// track versions by hand.
///
/// <para>
/// A test that *is* about staleness should send a version of its own rather
/// than use these helpers — they always send the current one, so they can
/// never provoke a 409.
/// </para>
/// </summary>
internal static class LayoutRequests
{
    internal static async Task<int> VersionAsync(HttpClient layouts, Guid layoutIdentifier)
    {
        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.EnsureSuccessStatusCode();
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("version").GetInt32();
    }

    internal static async Task<HttpResponseMessage> PostAsync(
        HttpClient layouts, Guid layoutIdentifier, string relativeUrl)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/layouts/{layoutIdentifier}/{relativeUrl}");
        await AddPreconditionAsync(request, layouts, layoutIdentifier);

        return await layouts.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> PatchAsync(
        HttpClient layouts, Guid layoutIdentifier, string relativeUrl, object body)
    {
        HttpRequestMessage request = new(HttpMethod.Patch, $"/layouts/{layoutIdentifier}/{relativeUrl}")
        {
            Content = JsonContent.Create(body),
        };
        await AddPreconditionAsync(request, layouts, layoutIdentifier);

        return await layouts.SendAsync(request);
    }

    private static async Task AddPreconditionAsync(
        HttpRequestMessage request, HttpClient layouts, Guid layoutIdentifier)
    {
        int version = await VersionAsync(layouts, layoutIdentifier);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
    }
}
