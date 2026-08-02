using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Sends a mutating rule request with the <c>If-Match</c> precondition the API
/// requires (ADR-0113 Layer 1). Mirrors the layout, overlay and variable
/// helpers.
///
/// <para>
/// Deliberately offers no dry-run helper. <c>POST /rules/{name}/dry-run</c> is
/// a POST that persists nothing — it sits in the server's *reads* group behind
/// the read scope — so it neither needs nor accepts a precondition. Giving it
/// a helper here would invite someone to add one.
/// </para>
/// </summary>
internal static class RuleRequests
{
    internal static async Task<int> VersionAsync(HttpClient rules, string name)
    {
        HttpResponseMessage fetched = await rules.GetAsync($"/rules/{name}");
        fetched.EnsureSuccessStatusCode();
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("version").GetInt32();
    }

    internal static HttpRequestMessage Conditional(string name, string action, int version)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/rules/{name}/{action}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        return request;
    }

    internal static async Task<HttpResponseMessage> PostAsync(HttpClient rules, string name, string action)
    {
        return await rules.SendAsync(Conditional(name, action, await VersionAsync(rules, name)));
    }
}
