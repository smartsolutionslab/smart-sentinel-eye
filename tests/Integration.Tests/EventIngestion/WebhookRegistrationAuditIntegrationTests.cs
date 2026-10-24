using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 445 (#3110) — a webhook credential is audited when it is issued.
///
/// <para>
/// Every absence assertion here waits for a positive control's row first. The
/// audit row arrives eventually, through the bus, so "zero rows" read straight
/// after a refusal passes trivially against a consumer that has not run yet.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WebhookRegistrationAuditIntegrationTests(AspireFixture aspire)
{
    private const string EventKind = "WebhookIntegrationRegisteredV1";
    private const string MunichOperator = "op-3@munich.test";
    private const string MunichOperatorPassword = SeededCredentials.Op3Munich;
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string DresdenOperatorPassword = SeededCredentials.OpDresden;
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string MultiFabOperatorPassword = SeededCredentials.OpMulti;

    [Fact]
    public async Task A_registration_is_audited_once_naming_the_operator_the_fab_and_never_the_credential()
    {
        var name = UniqueName("audited");
        using var munich = await ClientFor(MunichOperator, MunichOperatorPassword);

        var created = await munich.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var identifier = body.GetProperty("identifier").GetGuid();
        var token = body.GetProperty("token").GetString()!;

        var row = await PollForRowAsync(name);

        (await CountAsync(name)).ShouldBe(1);
        row.ResourceKind.ShouldBe("webhook-integration");
        row.Fab.ShouldBe("munich");
        row.Actor.ShouldBe(await SubjectOfAsync(MunichOperator, MunichOperatorPassword));
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("Integration").GetGuid().ShouldBe(identifier);
        payload.RootElement.GetProperty("IntegrationName").GetString().ShouldBe(name);
        payload.RootElement.GetProperty("DefaultKind").GetString().ShouldBe("WebhookAlarm");
        payload.RootElement.TryGetProperty("RegisteredAt", out _).ShouldBeTrue();

        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var wholeRow = await WholeRowTextAsync(name);
        wholeRow.ShouldNotContain(token);
        wholeRow.ShouldNotContain(hash);
    }

    [Fact]
    public async Task A_taken_name_is_refused_and_leaves_the_count_at_one()
    {
        var name = UniqueName("conflict");
        using var munich = await ClientFor(MunichOperator, MunichOperatorPassword);
        (await munich.PostAsJsonAsync("/webhook-integrations", new { name, defaultKind = "WebhookAlarm" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await PollForRowAsync(name);

        var duplicate = await munich.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await AwaitPositiveControlAsync(munich);
        (await CountAsync(name)).ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_fab_or_an_ambiguous_fab_audits_nothing()
    {
        var forbiddenName = UniqueName("forbidden");
        var ambiguousName = UniqueName("ambiguous");

        using var dresden = await ClientFor(DresdenOperator, DresdenOperatorPassword);
        (await dresden.PostAsJsonAsync(
            "/webhook-integrations?fabId=munich", new { name = forbiddenName, defaultKind = "WebhookAlarm" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var multi = await ClientFor(MultiFabOperator, MultiFabOperatorPassword);
        (await multi.PostAsJsonAsync(
            "/webhook-integrations", new { name = ambiguousName, defaultKind = "WebhookAlarm" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var munich = await ClientFor(MunichOperator, MunichOperatorPassword);
        await AwaitPositiveControlAsync(munich);

        (await CountAsync(forbiddenName)).ShouldBe(0);
        (await CountAsync(ambiguousName)).ShouldBe(0);
    }

    private async Task AwaitPositiveControlAsync(HttpClient munich)
    {
        var control = UniqueName("control");
        (await munich.PostAsJsonAsync("/webhook-integrations", new { name = control, defaultKind = "WebhookAlarm" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await PollForRowAsync(control);
    }

    private async Task<AuditRow> PollForRowAsync(string name)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var context = await aspire.CreateAuditObservabilityDbContextAsync();
            var rows = await context.Database
                .SqlQuery<AuditRow>($"""
                    SELECT resource_kind::text AS "ResourceKind",
                           fab_id::text AS "Fab",
                           actor_identifier AS "Actor",
                           payload::text AS "Payload"
                    FROM audit_events
                    WHERE event_kind = {EventKind}
                      AND resource_identifier = {name}
                    """)
                .ToListAsync();

            if (rows.Count > 0)
            {
                return rows[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"No {EventKind} audit row for {name} within 30s.");
    }

    private async Task<int> CountAsync(string name)
    {
        await using var context = await aspire.CreateAuditObservabilityDbContextAsync();
        var counts = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM audit_events
                WHERE event_kind = {EventKind}
                  AND resource_identifier = {name}
                """)
            .ToListAsync();
        return counts[0];
    }

    private async Task<string> WholeRowTextAsync(string name)
    {
        await using var context = await aspire.CreateAuditObservabilityDbContextAsync();
        var rows = await context.Database
            .SqlQuery<string>($"""
                SELECT e::text AS "Value"
                FROM audit_events e
                WHERE event_kind = {EventKind}
                  AND resource_identifier = {name}
                """)
            .ToListAsync();
        return rows.ShouldHaveSingleItem();
    }

    private async Task<Guid> SubjectOfAsync(string username, string password)
    {
        var jwt = await aspire.GetAccessTokenAsync(username, password);
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        return claims.RootElement.GetProperty("sub").GetGuid();
    }

    private Task<HttpClient> ClientFor(string username, string password) =>
        aspire.CreateAuthenticatedClientAsync("event-ingestion", username, password);

    private static string UniqueName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant()[..Math.Min(63, prefix.Length + 20)];

    private sealed record AuditRow(string ResourceKind, string Fab, Guid Actor, string Payload);
}
