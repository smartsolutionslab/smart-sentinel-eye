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

    /// <summary>
    /// Archives every one of <paramref name="names"/>, and answers how many it
    /// archived (#2004).
    ///
    /// <para>
    /// <b>Two requests per variable, not one.</b> Archiving carries an
    /// <c>If-Match</c> (ADR-0113), and a caller that drove a variable to an
    /// unknown version — a measurement run whose last write may or may not have
    /// landed — cannot supply it from memory. Reading first costs a round trip
    /// and removes a whole class of 409 that would leave residue behind while
    /// reporting success.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing is swallowed.</b> A failure to archive throws, because the
    /// caller asked for these variables to be gone and a silent partial sweep is
    /// how the residue accumulated in the first place.
    /// </para>
    /// </summary>
    internal static async Task<int> ArchiveAllAsync(
        HttpClient variables, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        int archived = 0;
        foreach (string name in names)
        {
            HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{name}", cancellationToken);
            fetched.EnsureSuccessStatusCode();
            JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            using HttpRequestMessage request =
                Conditional(HttpMethod.Post, name, "archive", payload.GetProperty("version").GetInt32());
            using HttpResponseMessage response = await variables.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            archived++;
        }

        return archived;
    }
}
