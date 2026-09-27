using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// The <c>/event-sources</c> wire shapes in one place, mirroring
/// <c>EventTypeRegistryApi</c> (spec 143, phase 4a extension), plus the
/// idempotent-set helper plan.md R1 calls for.
///
/// <para>
/// <b>Source is a closed four-value VO</b> (<c>plc</c>, <c>inference</c>,
/// <c>manual</c>, <c>webhook</c>), unlike spec 143's <c>Kind</c>, which is a
/// free-form string a test can make run-unique with <c>Guid.NewGuid()</c>.
/// There is no delete endpoint for a declared <c>(fab, source)</c> pair, so
/// once declared it stays declared in the shared dev database forever
/// (spec.md §6 step 13's manual clean-up note; plan.md R1). Every caller of
/// <see cref="DeclareAsync"/> in this suite either restores the pair to
/// <c>discovery</c> in a <c>finally</c> block, or targets a
/// <c>(fab, source)</c> pair reserved exclusively for that one test — see the
/// reservation comments in <c>EventSourceModeIntegrationTests</c>.
/// </para>
/// </summary>
internal static class EventSourceModeApi
{
    internal const string BerlinOperator = "op-berlin@berlin.test";
    internal const string DresdenOperator = "op-dresden@dresden.test";
    internal const string HamburgOperator = "op-hamburg@hamburg.test";
    internal const string MunichOperator = "op-3@munich.test";
    internal const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    internal const string OperatorPassword = "Operator1234";
    internal const string ResourceName = "event-ingestion";

    internal static Task<HttpResponseMessage> DeclareAsync(
        HttpClient client, string source, string mode, string? fabId = null) =>
        client.PostAsJsonAsync(Route(fabId), new { source, mode });

    internal static Task<HttpResponseMessage> DeclareWithKeyAsync(
        HttpClient client, string source, string mode, string key, string? fabId = null)
    {
        HttpRequestMessage request = new(HttpMethod.Post, Route(fabId))
        {
            Content = JsonContent.Create(new { source, mode }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return client.SendAsync(request);
    }

    /// <summary>
    /// <paramref name="expectedVersion"/> of <c>null</c> sends no
    /// <c>If-Match</c> at all — the 428 case.
    /// </summary>
    internal static Task<HttpResponseMessage> ChangeAsync(
        HttpClient client, string source, string mode, int? expectedVersion, string? fabId = null)
    {
        HttpRequestMessage request = new(
            HttpMethod.Put, $"/event-sources/{source}/mode{(fabId is null ? string.Empty : $"?fabId={fabId}")}")
        {
            Content = JsonContent.Create(new { mode }),
        };
        if (expectedVersion is { } version)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        }

        return client.SendAsync(request);
    }

    internal static Task<HttpResponseMessage> ListAsync(HttpClient client, string? fabId = null) =>
        client.GetAsync(Route(fabId));

    internal static async Task<JsonElement> ListRowsAsync(HttpClient client, string message)
    {
        HttpResponseMessage listed = await ListAsync(client);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, message);

        return await listed.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static JsonElement? RowFor(JsonElement rows, string source) =>
        rows.EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(row => row!.Value.GetProperty("source").GetString() == source);

    internal static async Task<string?> TitleOfAsync(HttpResponseMessage response)
    {
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        return problem.ValueKind == JsonValueKind.Object
               && problem.TryGetProperty("title", out JsonElement title)
            ? title.GetString()
            : null;
    }

    /// <summary>
    /// Sets <paramref name="source"/>'s mode to <paramref name="mode"/>
    /// idempotently: reads the current listing, then <c>PUT</c>s with the
    /// row's own version if it already exists, or <c>POST</c>s a fresh
    /// declaration if it does not (plan.md R1). Safe to call from any test's
    /// arrange step, any number of times, in any order.
    /// </summary>
    internal static async Task SetAsync(HttpClient client, string source, string mode)
    {
        JsonElement rows = await ListRowsAsync(client, $"reading current modes before setting '{source}'");
        JsonElement? existing = RowFor(rows, source);

        if (existing is { } row)
        {
            int version = row.GetProperty("version").GetInt32();
            if (row.GetProperty("mode").GetString() == mode)
            {
                return; // already at the wanted mode; PUT would be a no-op anyway
            }

            HttpResponseMessage changed = await ChangeAsync(client, source, mode, version);
            changed.StatusCode.ShouldBe(
                HttpStatusCode.OK, $"setting '{source}' to '{mode}': {await changed.Content.ReadAsStringAsync()}");
        }
        else
        {
            HttpResponseMessage declared = await DeclareAsync(client, source, mode);
            declared.StatusCode.ShouldBe(
                HttpStatusCode.Created, $"declaring '{source}' as '{mode}': {await declared.Content.ReadAsStringAsync()}");
        }
    }

    internal static Task RestoreDiscoveryAsync(HttpClient client, string source) => SetAsync(client, source, "discovery");

    private static string Route(string? fabId) =>
        fabId is null ? "/event-sources" : $"/event-sources?fabId={fabId}";
}
