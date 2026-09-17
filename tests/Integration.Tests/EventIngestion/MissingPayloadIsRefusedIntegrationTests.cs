using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 173 (#2203) — <c>Payload.From(body.Payload.GetRawText())</c> at
/// <c>EventsEndpoints.Writes.cs:85</c> (<c>POST /events/manual</c>) and
/// <c>:162</c> (<c>POST /events/webhook/{name}</c>) sits inside <c>catch
/// (ArgumentException ex)</c>. A body omitting <c>payload</c> leaves that
/// <c>JsonElement</c> at <c>JsonValueKind.Undefined</c>; <c>GetRawText()</c>
/// throws <see cref="InvalidOperationException"/>, uncaught, so the minimal
/// API answers 500 where every other bad field on the same endpoints
/// answers 400 <c>EVENT_INVALID_INPUT</c>.
/// </summary>
[Collection(AspireCollection.Name)]
public class MissingPayloadIsRefusedIntegrationTests(AspireFixture aspire)
{
    private const string ManualOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";
    private const string WebhookFab = "munich";

    [Fact]
    public async Task A_manual_ingest_with_no_payload_property_answers_400_not_500()
    {
        using HttpClient events = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", ManualOperator, OperatorPassword);

        HttpResponseMessage response = await events.PostAsJsonAsync(
            "/events/manual", ManualBodyMissingPayload());

        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await BodyAsync(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("EVENT_INVALID_INPUT");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("payload", Case.Insensitive);
    }

    [Fact]
    public async Task A_webhook_delivery_with_no_payload_property_answers_400_not_500()
    {
        string name = $"missing-payload-{Guid.NewGuid():N}"[..32];
        string token = await RegisterStaticHashIntegrationAsync(name);

        using HttpRequestMessage request = new(
            HttpMethod.Post, $"/events/webhook/{name}?fabId={WebhookFab}")
        {
            Content = JsonContent.Create(WebhookBodyMissingPayload()),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response = await aspire.EventIngestion.SendAsync(request);

        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await BodyAsync(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("EVENT_INVALID_INPUT");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("payload", Case.Insensitive);
    }

    /// <summary>
    /// Pins that the fix does not turn an auth refusal into a 400 or a 500
    /// that leaks how far the body got — no bearer token must still answer
    /// 401 before anything is read or written.
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_before_the_body_is_read()
    {
        string deviceId = $"anon-refused-{Guid.NewGuid():N}"[..24];
        using HttpClient anonymous = aspire.CreateServiceClient("event-ingestion");

        HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            "/events/manual", ManualBodyMissingPayload(deviceId));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await BodyAsync(response));

        (await DeadLetterCountAsync(deviceId)).ShouldBe(0, "an anonymous refusal wrote a dead letter");
        (await EventCountAsync(deviceId)).ShouldBe(0, "an anonymous refusal wrote an event");
    }

    private async Task<string> RegisterStaticHashIntegrationAsync(string name)
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("event-ingestion");
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "MissingPayloadProbe" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private async Task<long> DeadLetterCountAsync(string needle)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM dead_letters WHERE raw_payload LIKE {0}", $"%{needle}%")
            .SingleAsync();
    }

    private async Task<long> EventCountAsync(string deviceId)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE device_id = {0}", deviceId)
            .SingleAsync();
    }

    private static object ManualBodyMissingPayload(string? deviceId = null) => new
    {
        deviceId = deviceId ?? "missing-payload-device",
        kind = "ManualMissingPayload",
        occurredAt = DateTimeOffset.UtcNow,
    };

    private static object WebhookBodyMissingPayload() => new
    {
        kind = "WebhookMissingPayload",
        occurredAt = DateTimeOffset.UtcNow,
    };

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs("event-ingestion")}";
}
