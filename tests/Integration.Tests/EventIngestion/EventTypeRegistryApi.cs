using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// The registry's wire shapes in one place, so the three adversarial suites
/// beside <see cref="EventTypeRegistryIntegrationTests"/> do not each keep a
/// private copy of a route that does not exist yet (spec 143, phase 4a
/// extension).
///
/// <para>
/// <b><see cref="IdentifierOfAsync"/> reads a bare GUID.</b> <c>plan.md</c> §5
/// routes the create through <c>IdempotentRequest.ExecuteCreateAsync</c>, whose
/// response is <c>Results.Created(location, identifier)</c> — confirmed a bare
/// GUID (spec.md corrected in commit 8753b205; it no longer claims the 201 body
/// carries an object).
/// </para>
/// </summary>
internal static class EventTypeRegistryApi
{
    internal const string DresdenOperator = "op-dresden@dresden.test";
    internal const string DresdenOperatorPassword = SeededCredentials.OpDresden;
    internal const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    internal const string MultiFabOperatorPassword = SeededCredentials.OpMulti;
    internal const string ResourceName = "event-ingestion";

    /// <summary>
    /// Resolves the seeded password for one of this class's own operator
    /// usernames, so a helper that is handed a username variable (rather
    /// than one of the named constants directly) can still authenticate as
    /// that account (spec 325, #2510).
    /// </summary>
    internal static string PasswordFor(string username) => username switch
    {
        DresdenOperator => DresdenOperatorPassword,
        MultiFabOperator => MultiFabOperatorPassword,
        _ => throw new ArgumentOutOfRangeException(nameof(username), username, "no seeded password for this username"),
    };

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
    /// turns on. <paramref name="fabId"/> is omitted by default, mirroring
    /// <see cref="RegisterAsync"/>.
    /// </summary>
    internal static Task<HttpResponseMessage> RetireAsync(
        HttpClient client, string kind, int? expectedVersion, string? fabId = null)
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"/event-types/{kind}{(fabId is null ? string.Empty : $"?fabId={fabId}")}");
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

        return body.GetGuid();
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
