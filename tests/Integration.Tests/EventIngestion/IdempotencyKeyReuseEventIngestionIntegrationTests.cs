using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventSourceModeApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 302 (#2424/#2492) on the remaining two EventIngestion call sites.
///
/// <para>
/// <c>POST /event-sources</c> used to carry a local patch
/// (<c>EventSourcesEndpoints.DeclareEndpoint</c> folded the resolved fab and
/// source into the scope's <c>Endpoint</c> string) that happened to close this
/// exact case for <i>that one endpoint</i> — a different source made a
/// different <c>Endpoint</c>, so the second request was a fresh reservation,
/// not a replay, and was answered 201 rather than refused. Spec 302 reverts
/// that patch (it can overflow <c>endpoint VARCHAR(128)</c> and did not cover
/// the rest of the body) in favour of the general fingerprint mechanism, so
/// the behaviour this test wants — <c>422</c>, nothing declared — is still
/// true after the fix even though the <i>reason</i> changed.
/// </para>
///
/// <para>
/// <c>POST /events/manual</c> carried no such patch: a different event under
/// the same key used to replay the first event's identifier, with a fresh
/// <c>EventIdentifier</c> never actually stored.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotencyKeyReuseEventIngestionIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "berlin";

    /// <summary>
    /// Declares <c>plc</c> under a key, then reuses that key for <c>webhook</c>
    /// — a different source, same fab, same caller. This used to answer 201 for
    /// both (the local endpoint-string fold made the second call a fresh
    /// reservation); the fix makes it answer 422 and leave webhook
    /// undeclared. Restores both sources to <c>discovery</c> in a
    /// <c>finally</c>, mirroring <c>EventSourceModeIntegrationTests</c>'s own
    /// reuse-with-key case.
    /// </summary>
    [Fact]
    public async Task A_key_reused_to_declare_a_different_source_is_refused()
    {
        string key = $"key-{Guid.CreateVersion7():N}";
        using HttpClient berlin = await ClientFor(BerlinOperator);

        try
        {
            HttpResponseMessage first = await DeclareWithKeyAsync(berlin, "plc", "strict", key, Fab);
            first.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(first));

            HttpResponseMessage second = await DeclareWithKeyAsync(berlin, "webhook", "strict", key, Fab);

            second.StatusCode.ShouldBe(
                HttpStatusCode.UnprocessableEntity,
                "a key already used to declare plc must not be honoured for webhook: " + await Diagnose(second));
            (await TitleOfAsync(second)).ShouldBe("IDEMPOTENCY_KEY_REUSED");

            JsonElement rows = await ListRowsAsync(berlin, await Diagnose(second));
            RowFor(rows, "webhook").ShouldBeNull("webhook must never have been declared by the refused request");
        }
        finally
        {
            await UndeclareAsync(berlin, "plc");
            await UndeclareAsync(berlin, "webhook");
        }
    }

    /// <summary>
    /// Two different manual events under one key. The second request used to
    /// replay the first event's identifier, discarding the second event's own
    /// (fresh) <c>EventIdentifier</c> and storing nothing new; the fix refuses
    /// it instead.
    /// </summary>
    [Fact]
    public async Task A_key_reused_for_a_different_manual_event_is_refused()
    {
        string key = $"key-{Guid.CreateVersion7():N}";
        string firstKind = $"T302First{Guid.NewGuid():N}"[..24];
        string secondKind = $"T302Second{Guid.NewGuid():N}"[..24];
        using HttpClient events = await ClientFor(BerlinOperator);

        HttpResponseMessage first = await IngestManualAsync(events, firstKind, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(first));

        HttpResponseMessage second = await IngestManualAsync(events, secondKind, key);

        second.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used for the first event must not be honoured for a different one: "
            + await Diagnose(second));
        (await TitleOfAsync(second)).ShouldBe("IDEMPOTENCY_KEY_REUSED");

        await Task.Delay(TimeSpan.FromSeconds(2));
        (await CountOfKindAsync(events, secondKind)).ShouldBe(
            0, "the second event must never have been stored by the refused request");
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync(ResourceName, username, OperatorPassword);

    private static Task<HttpResponseMessage> IngestManualAsync(HttpClient events, string kind, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/events/manual?fabId={Fab}")
        {
            Content = JsonContent.Create(new
            {
                deviceId = "t302-device",
                kind,
                occurredAt = DateTimeOffset.UtcNow,
                payload = new { note = "spec 302" },
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return events.SendAsync(request);
    }

    private static async Task<int> CountOfKindAsync(HttpClient events, string kind)
    {
        HttpResponseMessage listed = await events.GetAsync($"/events?fabId={Fab}&kind={kind}");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return page.GetProperty("items").GetArrayLength();
    }

    private async Task<string> Diagnose(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs(ResourceName)}";
}
