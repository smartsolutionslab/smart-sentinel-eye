using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// #2814 — <c>POST /events/manual</c> gets the same immediate refusal
/// <c>POST /events/webhook/{name}</c> already gives a revoked integration's
/// own bearer, closing the gap ADR-0160's generic <c>DisabledAt</c> check
/// leaves open while Keycloak's own disable is still in flight (spec 264,
/// widened by #2629 to ~70 min).
///
/// <para>
/// <b>Why this reuses <c>management-web</c>, and why it detaches it
/// afterward.</b> JWT-mode webhook fixtures across this folder mint through
/// <c>management-web</c> rather than a real rotated client, because the real
/// rotate path issues a credential with no usable <c>groups</c> claim
/// (#2206) — see <c>WebhookRevocationRefusesDeliveryIntegrationTests</c>.
/// That makes <c>management-web</c> a client id several unrelated test files
/// seed a <see cref="WebhookIntegration"/> row against. Before this issue,
/// nothing ever looked up a row by <c>KeycloakClientId</c> alone, so sharing
/// it was harmless. This PR's repository check
/// (<c>WebhookIntegrationRepository.IsRevokedByKeycloakClientIdAsync</c>) is
/// fail-closed: it refuses if <b>any</b> row sharing a client id is revoked,
/// not just the most recent one — correct in production, where the id is
/// derived from the integration's own unique name and never collides, but it
/// means a revoked row left seeded here with <c>management-web</c> would make
/// <b>every real operator's token</b> (azp is also <c>management-web</c>)
/// look revoked to <c>/events/manual</c> for the rest of this run, no matter
/// how many fresh rows are seeded afterward. The closing step below instead
/// moves the revoked row itself off the shared client id;
/// <c>WebhookRevocationRefusesDeliveryIntegrationTests</c> gained the same
/// step for the same reason. It runs in a <c>finally</c> so it still happens
/// if the assertion above it fails.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ManualIngestRefusesRevokedWebhookCallerIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";
    private const string JwtClientId = "management-web";
    private const string JwtScope = "openid sse.events.write";

    private static readonly JsonElement Payload =
        JsonDocument.Parse("""{"note":"spec 336 manual-ingest revocation check"}""").RootElement;

    [Fact]
    public async Task A_revoked_webhook_integrations_token_is_refused_by_manual_ingest_too()
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName("manual-jwt-revoked");
        await SeedJwtIntegrationAsync(name);
        string jwt = await aspire.GetAccessTokenForClientAsync(
            JwtClientId, AspireFixture.AdminUsername, AspireFixture.AdminPassword, JwtScope);

        // Control: the same token, before revocation, is accepted — proving
        // the refusal below is the revocation, not a scope/fab mismatch.
        HttpResponseMessage accepted = await PostManualAsync(jwt);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(accepted));

        await RevokeAsync(admin, name);

        try
        {
            HttpResponseMessage refused = await PostManualAsync(jwt);
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await DiagnoseAsync(refused));
        }
        finally
        {
            // Moves the now-revoked row off "management-web" entirely (see the
            // class doc) so it stops being load-bearing for every later
            // /events/manual test in this run — even if the assertion above
            // just failed.
            await DetachFromSharedClientAsync(name);
        }
    }

    private async Task SeedJwtIntegrationAsync(string name)
    {
        await using EventIngestionDbContext context = await aspire.CreateEventIngestionDbContextAsync();

        SystemClock clock = new();
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From(name), FabIdentifier.From(Fab), Kind.From("WebhookAlarm"), clock);
        integration.MarkAsRotated(KeycloakClientIdentifier.From(JwtClientId), clock);
        integration.ClearPendingEvents();

        context.WebhookIntegrations.Add(integration);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// <c>MarkAsRotated</c> has no revoked guard, so this moves the row named
    /// <paramref name="name"/> onto a client id derived from its own (unique)
    /// name — off <c>management-web</c> for good, rather than leaving it
    /// there and hoping a fresher sibling row outranks it (#2814;
    /// <c>IsRevokedByKeycloakClientIdAsync</c> is fail-closed and does not
    /// rank rows by recency at all).
    /// </summary>
    private async Task DetachFromSharedClientAsync(string name)
    {
        await using EventIngestionDbContext context = await aspire.CreateEventIngestionDbContextAsync();
        WebhookIntegrationName parsed = WebhookIntegrationName.From(name);
        WebhookIntegration integration = await context.WebhookIntegrations
            .SingleAsync(candidate => candidate.Name == parsed);

        integration.MarkAsRotated(KeycloakClientIdentifier.From($"webhook-{name}"), new SystemClock());
        integration.ClearPendingEvents();

        await context.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PostManualAsync(string bearer)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"/events/manual?fabId={Fab}")
        {
            Content = JsonContent.Create(new
            {
                deviceId = "manual-revocation-check",
                kind = "WebhookAlarm",
                occurredAt = DateTimeOffset.UtcNow,
                payload = Payload,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        return await aspire.EventIngestion.SendAsync(request);
    }

    /// <summary>
    /// Revoke carrying the version the listing just reported (ADR-0113), same
    /// idiom as <c>WebhookRevocationRefusesDeliveryIntegrationTests.RevokeAsync</c>.
    /// </summary>
    private async Task RevokeAsync(HttpClient admin, string name)
    {
        int version = (await FindAsync(admin, name)).GetProperty("version").GetInt32();

        using HttpRequestMessage request = new(HttpMethod.Delete, $"/webhook-integrations/{name}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        HttpResponseMessage revoked = await admin.SendAsync(request);

        revoked.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(revoked));
    }

    private static async Task<JsonElement> FindAsync(HttpClient admin, string name)
    {
        HttpResponseMessage listed = await admin.GetAsync("/webhook-integrations?includeRevoked=false");
        listed.EnsureSuccessStatusCode();

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return rows.EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("name").GetString(), name, StringComparison.Ordinal));
    }

    private static string UniqueName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant()[..Math.Min(63, prefix.Length + 33)];

    private Task<string> DiagnoseAsync(HttpResponseMessage response) =>
        aspire.DiagnoseAsync("event-ingestion", response);
}
