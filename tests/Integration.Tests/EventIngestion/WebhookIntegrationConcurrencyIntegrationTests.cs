using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// ADR-0113 Layer 1 for EventIngestion (spec 012 T044), against the real
/// stack — so the version the conflict test races on is one the EF
/// interceptor actually moved, which no Application-layer fake reproduces.
///
/// <para>
/// Unlike Identity's disables, this context can be raced honestly. A revoke
/// leaves the row reachable by name, and <c>MarkAsRotated</c> — which
/// arrives from Identity when an admin rotates the integration's bearer onto
/// Keycloak — moves the version underneath a caller still holding the
/// pre-rotation read. That is the lost update the gate exists for, and it is
/// what the 409 test below reproduces rather than settling for a version the
/// integration never had.
/// </para>
///
/// <para>
/// The rotation is applied through a second DbContext carrying the
/// interceptor, following <c>AggregateVersionConflictIntegrationTests</c>.
/// Driving it through Identity's rotate endpoint would exercise the same
/// bump one hop further out, but the hop is Wolverine over RabbitMQ — the
/// test would have to poll for eventual consistency, and a timeout would
/// read as a concurrency failure rather than a delivery one.
/// </para>
///
/// <para>
/// No per-test reset: this context has no reset helper, and the suite's
/// existing webhook tests mint a unique name per test instead. Same here.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WebhookIntegrationConcurrencyIntegrationTests(AspireFixture aspire)
{
    /// <summary>
    /// Asserts the listed version against one the **running service** moved,
    /// not against a bound no value can violate. `>= 0` used to be the whole
    /// assertion, which a projection emitting a constant would also satisfy —
    /// and a constant is exactly the regression that would make every revoke
    /// 409 with no way for an operator to remove a compromised credential.
    /// </summary>
    [Fact]
    public async Task The_listed_version_tracks_what_the_service_persisted()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName();
        await RegisterAsync(events, name);

        int onCreation = (await FindAsync(events, name)).GetProperty("version").GetInt32();
        onCreation.ShouldBe(0, "an Added root is not bumped by the interceptor");

        // Revoking through the API is the only way to observe a version the
        // service itself moved; every other test here manufactures the bump
        // with its own DbContext and so cannot detect the interceptor being
        // unregistered for this context.
        (await events.SendAsync(Conditional(name, onCreation)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        int afterRevoke = (await FindAsync(events, name, includeRevoked: true))
            .GetProperty("version").GetInt32();

        afterRevoke.ShouldBe(onCreation + 1);
    }

    [Fact]
    public async Task A_revoke_without_If_Match_is_refused_with_428_and_leaves_the_integration_live()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName();
        await RegisterAsync(events, name);

        HttpResponseMessage refused = await events.DeleteAsync($"/webhook-integrations/{name}");

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // includeRevoked: true is load-bearing. The default listing filters
        // RevokedAt == null server-side, so a revoke that had wrongly landed
        // would drop the row out entirely rather than surface a non-null
        // revokedAt — the assertion could only ever see Null either way.
        (await FindAsync(events, name, includeRevoked: true))
            .GetProperty("revokedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_revoke_superseded_by_a_rotation_is_refused_with_409()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName();
        await RegisterAsync(events, name);

        int readAt = (await FindAsync(events, name)).GetProperty("version").GetInt32();
        await RotateOutOfBandAsync(name);

        HttpResponseMessage refused = await events.SendAsync(Conditional(name, readAt));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("WEBHOOK_INTEGRATION_STALE");

        // The rotation survives and the integration is still live — a
        // status-only assertion would pass even if the revoke had landed.
        // Queried with includeRevoked so a landed revoke would be visible
        // rather than silently filtered out of the result.
        JsonElement row = await FindAsync(events, name, includeRevoked: true);
        row.GetProperty("revokedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("version").GetInt32().ShouldBeGreaterThan(readAt);
    }

    [Fact]
    public async Task A_revoke_carrying_the_listed_version_succeeds()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName();
        await RegisterAsync(events, name);
        int version = (await FindAsync(events, name)).GetProperty("version").GetInt32();

        HttpResponseMessage revoked = await events.SendAsync(Conditional(name, version));

        revoked.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(revoked));
        (await FindAsync(events, name, includeRevoked: true))
            .GetProperty("revokedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    private static async Task RegisterAsync(HttpClient events, string name)
    {
        HttpResponseMessage created = await events.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// Applies the rotation Identity would publish, through a context that
    /// carries the interceptor — so the version moves exactly as it does in
    /// the running service.
    /// </summary>
    private async Task RotateOutOfBandAsync(string name)
    {
        string connectionString = await aspire.App
            .GetConnectionStringAsync(AspireFixture.EventIngestionConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{AspireFixture.EventIngestionConnectionName}' was not provisioned by Aspire.");

        DbContextOptionsBuilder<EventIngestionDbContext> options = new();
        options.UseNpgsql(connectionString);
        options.AddInterceptors(new AggregateVersionInterceptor());

        await using EventIngestionDbContext context = new(options.Options);
        WebhookIntegrationName parsed = WebhookIntegrationName.From(name);
        WebhookIntegration integration = await context.WebhookIntegrations
            .FirstAsync(candidate => candidate.Name == parsed);

        integration.MarkAsRotated(KeycloakClientIdentifier.From($"webhook-{name}"), new SystemClock());
        integration.ClearPendingEvents();
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The list is shared with every other test in the run, so the row is
    /// found by name rather than by position.
    /// </summary>
    private static async Task<JsonElement> FindAsync(
        HttpClient events, string name, bool includeRevoked = false)
    {
        HttpResponseMessage listed = await events.GetAsync(
            $"/webhook-integrations?includeRevoked={includeRevoked}");
        listed.EnsureSuccessStatusCode();

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return rows.EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("name").GetString(), name, StringComparison.Ordinal));
    }

    private static HttpRequestMessage Conditional(string name, int version)
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"/webhook-integrations/{name}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        return request;
    }

    private static string UniqueName() => $"t044-{Guid.NewGuid():N}".ToLowerInvariant()[..24];

    /// <summary>
    /// A bare "500" tells a reader nothing, and CI has no other route to the
    /// service's stack trace. Attach the response body and the service's
    /// recent output so an unexpected status is diagnosable from the CI log.
    /// </summary>
    private async Task<string> DiagnoseAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return $"body: {body}{Environment.NewLine}event-ingestion log:{Environment.NewLine}{aspire.RecentLogs("event-ingestion")}";
    }
}
