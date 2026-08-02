using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Sends a mutating system-variable request with the <c>If-Match</c>
/// precondition the API requires (ADR-0113 Layer 1). Mirrors
/// <see cref="LayoutRequests"/> and <see cref="OverlayRequests"/>.
///
/// <para>
/// Reads the current version first, so a test that is not about staleness
/// does not track versions by hand. A test that *is* about staleness must
/// send its own version — these helpers always send the current one, so they
/// can never provoke a 409.
/// </para>
/// </summary>
internal static class VariableRequests
{
    internal static async Task<int> VersionAsync(HttpClient variables, string name)
    {
        HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{name}");
        fetched.EnsureSuccessStatusCode();
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("version").GetInt32();
    }

    internal static HttpRequestMessage Conditional(HttpMethod method, string name, string relativeUrl, int version)
    {
        string url = relativeUrl.Length == 0
            ? $"/system-variables/{name}"
            : $"/system-variables/{name}/{relativeUrl}";
        HttpRequestMessage request = new(method, url);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        return request;
    }

    internal static async Task<HttpResponseMessage> SetValueAsync(HttpClient variables, string name, string value)
    {
        HttpRequestMessage request = Conditional(
            HttpMethod.Put, name, "value", await VersionAsync(variables, name));
        request.Content = JsonContent.Create(new { value });

        return await variables.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> ArchiveAsync(HttpClient variables, string name)
    {
        return await variables.SendAsync(
            Conditional(HttpMethod.Post, name, "archive", await VersionAsync(variables, name)));
    }
}
