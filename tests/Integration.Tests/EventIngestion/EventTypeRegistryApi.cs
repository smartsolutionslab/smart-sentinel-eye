using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// The registry's wire shapes in one place, so the three adversarial suites
/// beside <see cref="EventTypeRegistryIntegrationTests"/> do not each keep a
/// private copy of a route that does not exist yet (spec 143, phase 4a
/// extension).
///
/// <para>
/// <b><see cref="IdentifierOfAsync"/> accepts two body shapes on purpose, and
/// that is a finding rather than tolerance.</b> <c>spec.md</c> §4 says the 201
/// body "carries the new eventTypeId, the kind, and fab"; <c>plan.md</c> §5
/// routes the create through <c>IdempotentRequest.ExecuteCreateAsync</c>, whose
/// response is <c>Results.Created(location, identifier)</c> — a bare GUID. The
/// two cannot both be built. Reading either here keeps every assertion in these
/// files about the behaviour it names instead of failing on a shape the spec
/// has not settled.
/// </para>
/// </summary>
internal static class EventTypeRegistryApi
{
    internal const string DresdenOperator = "op-dresden@dresden.test";
    internal const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    internal const string OperatorPassword = "Operator1234";
    internal const string ResourceName = "event-ingestion";

    internal static string UniqueKind() => $"EventType{Guid.NewGuid():N}";

    internal static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string kind, string? fabId = null) =>
        client.PostAsJsonAsync(Route(fabId), new { kind });

    internal static Task<HttpResponseMessage> RegisterWithKeyAsync(
        HttpClient client, string kind, string key, string? fabId = null)
    {
        HttpRequestMessage request = new(HttpMethod.Post, Route(fabId))
        {
            Content = JsonContent.Create(new { kind }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return client.SendAsync(request);
    }

    /// <summary>
    /// <paramref name="expectedVersion"/> of <c>null</c> sends no
    /// <c>If-Match</c> at all — the 428 case, and the one the disclosure
    /// ordering in <see cref="EventTypeRegistryAuthorizationIntegrationTests"/>
    /// turns on.
    /// </summary>
    internal static Task<HttpResponseMessage> RetireAsync(
        HttpClient client, string kind, int? expectedVersion)
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"/event-types/{kind}");
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

    internal static int CountOf(JsonElement rows, string kind) =>
        rows.EnumerateArray().Count(row => row.GetProperty("kind").GetString() == kind);

    internal static async Task<Guid> IdentifierOfAsync(HttpResponseMessage response)
    {
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.ValueKind == JsonValueKind.Object
            ? body.GetProperty("eventTypeId").GetGuid()
            : body.GetGuid();
    }

    internal static async Task<string?> TitleOfAsync(HttpResponseMessage response)
    {
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        return problem.ValueKind == JsonValueKind.Object
               && problem.TryGetProperty("title", out JsonElement title)
            ? title.GetString()
            : null;
    }

    private static string Route(string? fabId) =>
        fabId is null ? "/event-types" : $"/event-types?fabId={fabId}";
}
