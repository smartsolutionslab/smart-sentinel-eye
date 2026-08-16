using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 018 T001 — records what the leak actually does, <b>before</b> it is
/// closed. Temporary: deleted once the observations are on the PR.
///
/// <para>
/// Every other test in this feature asserts that something is now refused, and
/// a refusal proves nothing unless the thing was permitted a moment earlier.
/// This is also the only chance to see what reading another plant's events
/// returns, because after the change nothing can produce that response.
/// </para>
///
/// <para>
/// It asserts almost nothing on purpose — it <em>reports</em>. Assertions here
/// would have to encode the leak, and would then have to be deleted or
/// inverted, which is how a test that documents a defect turns into a test
/// that protects it.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventFabLeakBaselineTests(AspireFixture aspire, ITestOutputHelper output) : IAsyncLifetime
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    /// <summary>
    /// No database reset: this context has no reset helper, and none is needed
    /// — the run reports what a Dresden operator can reach rather than
    /// asserting an exact count, so pre-existing rows do not invalidate it.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Record_what_a_dresden_operator_can_reach_in_munich()
    {
        // Seed one munich event through the manual endpoint as the multi-fab
        // operator, who legitimately holds munich.
        using HttpClient seeder = await ClientFor(MultiFabOperator);
        HttpResponseMessage seeded = await seeder.PostAsJsonAsync(
            "/events/manual?fabId=munich",
            new
            {
                deviceId = "baseline-device",
                kind = "BaselineProbe",
                occurredAt = DateTimeOffset.UtcNow,
                payload = new { note = "spec 018 baseline" },
            });
        output.WriteLine($"seed POST /events/manual?fabId=munich -> {(int)seeded.StatusCode}");

        // Give the ingest channel a moment; this is a report, not an SLO test.
        await Task.Delay(TimeSpan.FromSeconds(2));

        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage list = await dresden.GetAsync("/events?fabId=munich");
        output.WriteLine($"GET /events?fabId=munich as op-dresden -> {(int)list.StatusCode}");
        if (list.IsSuccessStatusCode)
        {
            JsonElement page = await list.Content.ReadFromJsonAsync<JsonElement>();
            int count = page.TryGetProperty("items", out JsonElement items)
                ? items.GetArrayLength()
                : -1;
            output.WriteLine($"  munich events visible to a dresden-only operator: {count}");
        }

        HttpResponseMessage inject = await dresden.PostAsJsonAsync(
            "/events/manual?fabId=munich",
            new
            {
                deviceId = "dresden-injected",
                kind = "BaselineInjected",
                occurredAt = DateTimeOffset.UtcNow,
                payload = new { note = "injected into munich by a dresden operator" },
            });
        output.WriteLine($"POST /events/manual?fabId=munich as op-dresden -> {(int)inject.StatusCode}");

        HttpResponseMessage deadLetters = await dresden.GetAsync("/events/dead-letters");
        output.WriteLine($"GET /events/dead-letters as op-dresden -> {(int)deadLetters.StatusCode}");
        if (deadLetters.IsSuccessStatusCode)
        {
            JsonElement rows = await deadLetters.Content.ReadFromJsonAsync<JsonElement>();
            output.WriteLine($"  rejected deliveries visible, all plants: {rows.GetArrayLength()}");
        }

        // Deliberately no assertions. See the class summary.
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync("event-ingestion", username, OperatorPassword);
}
