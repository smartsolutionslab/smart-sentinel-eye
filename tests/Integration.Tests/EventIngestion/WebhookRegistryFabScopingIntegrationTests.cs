using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// #1545, amending spec 018 FR-016 — the webhook integration registry is
/// fab-owned.
///
/// <para>
/// Closing the delivery-side gap is not enough on its own. If the registry
/// stayed unscoped, one plant could still read another's integration names and
/// the version each needs to be revoked with — and then revoke them, which
/// stops that plant's machine ingest silently and is the sharper half of the
/// two.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WebhookRegistryFabScopingIntegrationTests(AspireFixture aspire)
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    [Fact]
    public async Task An_integration_is_registered_into_the_registering_operators_fab()
    {
        string name = UniqueName("dresden-owned");

        using HttpClient dresden = await ClientFor(DresdenOperator);
        HttpResponseMessage created = await dresden.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        // Read back through the multi-fab operator, who can see both plants and
        // so can tell dresden from the munich default everything else falls to.
        using HttpClient multi = await ClientFor(MultiFabOperator);
        JsonElement listed = await ListAsync(multi);

        listed.EnumerateArray()
            .Single(row => row.GetProperty("name").GetString() == name)
            .GetProperty("fab").GetString()
            .ShouldBe("dresden");
    }

    [Fact]
    public async Task A_multi_fab_operator_naming_no_fab_is_refused()
    {
        using HttpClient multi = await ClientFor(MultiFabOperator);

        HttpResponseMessage refused = await multi.PostAsJsonAsync(
            "/webhook-integrations", new { name = UniqueName("ambiguous"), defaultKind = "WebhookAlarm" });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("EVENT_FAB_REQUIRED");
    }

    [Fact]
    public async Task Registering_into_a_fab_the_caller_does_not_hold_is_refused()
    {
        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage refused = await dresden.PostAsJsonAsync(
            "/webhook-integrations?fabId=munich",
            new { name = UniqueName("cross-fab"), defaultKind = "WebhookAlarm" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dresden_operator_does_not_see_a_munich_integration()
    {
        string name = UniqueName("munich-owned");

        using HttpClient multi = await ClientFor(MultiFabOperator);
        (await multi.PostAsJsonAsync(
            "/webhook-integrations?fabId=munich", new { name, defaultKind = "WebhookAlarm" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using HttpClient dresden = await ClientFor(DresdenOperator);
        JsonElement listed = await ListAsync(dresden);

        listed.EnumerateArray()
            .Select(row => row.GetProperty("name").GetString())
            .ShouldNotContain(name);
    }

    /// <summary>
    /// The one that matters most: revoking another plant's integration stops
    /// its machine ingest, and nothing about the response should say the
    /// integration is there to be revoked.
    /// </summary>
    [Fact]
    public async Task Revoking_another_plants_integration_is_reported_as_one_that_never_existed()
    {
        string name = UniqueName("munich-revoke");

        using HttpClient multi = await ClientFor(MultiFabOperator);
        (await multi.PostAsJsonAsync(
            "/webhook-integrations?fabId=munich", new { name, defaultKind = "WebhookAlarm" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        JsonElement listed = await ListAsync(multi);
        int version = listed.EnumerateArray()
            .Single(row => row.GetProperty("name").GetString() == name)
            .GetProperty("version").GetInt32();

        using HttpClient dresden = await ClientFor(DresdenOperator);
        HttpResponseMessage hidden = await RevokeAsync(dresden, name, version);
        HttpResponseMessage absent = await RevokeAsync(dresden, UniqueName("never"), version);

        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound, await hidden.Content.ReadAsStringAsync());
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And still there afterwards, because a 404 that had revoked it anyway
        // would be the worst of both.
        JsonElement stillListed = await ListAsync(multi);
        stillListed.EnumerateArray()
            .Single(row => row.GetProperty("name").GetString() == name)
            .GetProperty("revokedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static async Task<HttpResponseMessage> RevokeAsync(HttpClient client, string name, int version)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, $"/webhook-integrations/{name}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ListAsync(HttpClient client)
    {
        HttpResponseMessage listed = await client.GetAsync("/webhook-integrations?includeRevoked=true");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());
        return await listed.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync("event-ingestion", username, OperatorPassword);

    private static string UniqueName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant()[..Math.Min(63, prefix.Length + 20)];
}
