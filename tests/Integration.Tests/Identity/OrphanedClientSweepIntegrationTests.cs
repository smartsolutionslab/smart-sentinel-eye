using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 320 (#2181) plan §8.1 — facts I0a, I0b, I1, I1k, I2, I3, I4; I5 added
/// by spec 337 (#2797). The
/// load-bearing red (I1/I1k) against the real stack: a planted orphan blocks
/// re-registration with 409 today, and after one pass the same request must
/// answer 201.
///
/// <para>
/// <b>I0a and I0b are controls, not the feature.</b> I0a proves the defect
/// spec §1 describes is live on this tree today (HTTP only — no new type
/// involved, so it is not itself a compile red). I0b proves plan §3's
/// premise — that Keycloak 26.6.4 records a usable
/// <c>createdTimestamp</c> on a client's service-account user — against the
/// real, pinned server, before anything relies on it. If I0b is red, plan
/// §3's premise is false here and the fallback (stamping an
/// <c>sse.createdAt</c> attribute) changes the handlers, which needs the
/// gate, not a workaround.
/// </para>
///
/// <para>
/// <b>Driven through the pass, not the hosted service.</b> Like
/// <see cref="KioskPrivilegeSweepStartupIntegrationTests"/>, this composes
/// <c>AddIdentityInfrastructure</c> in-process against the fixture's real
/// Keycloak — but unlike that file, it also points at the fixture's real
/// <c>identity-db</c> (S3 reads Postgres) and resolves
/// <see cref="OrphanedClientSweep"/> itself, with <see cref="IClock"/>
/// replaced by a fixed clock, and calls <c>SweepAsync</c> directly — the
/// hosted service adds only a <c>PeriodicTimer</c> around it (plan §5), which
/// phase 5's independent procedure (spec §9) exercises end to end.
/// </para>
///
/// <para>
/// <b>Assertions read the planted client's own state</b> (Keycloak's
/// <c>enabled</c> flag, a replay registration, a client_credentials grant),
/// not the pass's aggregate counts. The sweep's <c>GetStampedClientsAsync</c>
/// lists the <i>whole realm</i>, and this is a shared stack (plan §8.1's own
/// "Note on the shared stack") — other suites' legitimately-registered
/// clients always carry an active row and are never candidates, but
/// asserting <c>Disabled == 1</c> would still be a claim about a fleet this
/// test does not own.
/// </para>
///
/// <para>
/// Red today (I1, I1k, I2, I3, I4): compile — <c>OrphanedClientSweep</c> does
/// not exist on <c>develop</c>. Green at T011/T012.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OrphanedClientSweepIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";
    private static readonly TimeSpan PastGrace = OrphanedClientSweep.GraceWindow + TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WithinGrace = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ADayLater = TimeSpan.FromDays(1);

    private readonly RealmProbe realm = new(aspire);

    [Fact]
    public async Task I0a_a_planted_orphan_device_makes_registration_answer_409_device_already_registered()
    {
        string deviceIdentifier = NewSuffix("orphan");
        string clientId = $"plc-{deviceIdentifier}";
        CancellationToken cancellationToken = CancellationToken.None;

        await realm.PlantAsync(clientId, DeviceAttributes(deviceIdentifier), cancellationToken);
        try
        {
            using HttpClient munich = await MunichClientAsync();
            HttpResponseMessage response = await RegisterDeviceAsync(munich, deviceIdentifier);

            response.StatusCode.ShouldBe(
                HttpStatusCode.Conflict, await aspire.DiagnoseAsync("identity", response));
            string body = await response.Content.ReadAsStringAsync();
            body.ShouldContain(
                "DEVICE_ALREADY_REGISTERED",
                customMessage: "the control: a cancellation between creating the Keycloak client and "
                + $"saving the row must leave the device unable to register (spec 320 §1). Body: {body}");
        }
        finally
        {
            await realm.DeleteAsync(clientId, cancellationToken);
        }
    }

    [Fact]
    public async Task I0b_a_planted_clients_service_account_carries_a_createdTimestamp_near_the_test_clock()
    {
        string clientId = $"plc-{NewSuffix("pinned-version-check")}";
        CancellationToken cancellationToken = CancellationToken.None;
        DateTimeOffset before = DateTimeOffset.UtcNow;

        await realm.PlantAsync(clientId, DeviceAttributes("whatever"), cancellationToken);
        try
        {
            using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);
            JsonElement clients = await RealmProbe.ReadJsonAsync(
                admin, $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
                cancellationToken);
            string uuid = clients.EnumerateArray().Single().GetProperty("id").GetString()!;

            JsonElement serviceAccount = await RealmProbe.ReadJsonAsync(
                admin, $"admin/realms/{RealmProbe.Realm}/clients/{uuid}/service-account-user", cancellationToken);

            serviceAccount.TryGetProperty("createdTimestamp", out JsonElement createdTimestampProperty).ShouldBeTrue(
                "plan §3's whole premise — the only creation time Keycloak records for a client "
                + "belongs to its service-account user — is false on this pinned server if the "
                + "property is absent. Escalate rather than work around (plan §3's risk table).");

            DateTimeOffset createdAt = DateTimeOffset.FromUnixTimeMilliseconds(createdTimestampProperty.GetInt64());
            DateTimeOffset after = DateTimeOffset.UtcNow;

            createdAt.ShouldBeGreaterThanOrEqualTo(
                before - TimeSpan.FromMinutes(2),
                $"createdTimestamp ({createdAt:O}) must be near the test's own wall clock "
                + $"({before:O}..{after:O}); the sweep's grace window (10 min) assumes it is.");
            createdAt.ShouldBeLessThanOrEqualTo(
                after + TimeSpan.FromMinutes(2),
                $"createdTimestamp ({createdAt:O}) must be near the test's own wall clock "
                + $"({before:O}..{after:O}); the sweep's grace window (10 min) assumes it is.");
        }
        finally
        {
            await realm.DeleteAsync(clientId, cancellationToken);
        }
    }

    [Fact]
    public async Task I1_an_orphan_device_past_grace_is_disabled_and_re_registration_then_succeeds()
    {
        string deviceIdentifier = NewSuffix("orphan");
        string clientId = $"plc-{deviceIdentifier}";
        CancellationToken cancellationToken = CancellationToken.None;
        DateTimeOffset plantedAt = DateTimeOffset.UtcNow;

        await realm.PlantAsync(clientId, DeviceAttributes(deviceIdentifier), cancellationToken);
        try
        {
            await RunSweepAsync(plantedAt + PastGrace, cancellationToken);

            (await IsEnabledAsync(clientId, cancellationToken)).ShouldBeFalse(
                "a pass past the grace window must disable the orphan (spec 320 §2 US1)");

            using HttpClient munich = await MunichClientAsync();
            HttpResponseMessage response = await RegisterDeviceAsync(munich, deviceIdentifier);
            response.StatusCode.ShouldBe(
                HttpStatusCode.Created,
                "disabling the orphan must unblock #2728's replace path for the same identifier "
                + $"in the same fab: {await aspire.DiagnoseAsync("identity", response)}");

            JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>();
            string secret = created.GetProperty("clientSecret").GetString()!;

            (await CanAuthenticateAsync(clientId, secret)).ShouldBeTrue(
                "the fresh secret from the healed registration must actually work");
        }
        finally
        {
            await realm.DeleteAsync(clientId, cancellationToken);
        }
    }

    [Fact]
    public async Task I1k_an_orphan_kiosk_past_grace_is_disabled_and_re_enrollment_then_succeeds()
    {
        string clientId = $"kiosk-{NewSuffix("orphan")}";
        CancellationToken cancellationToken = CancellationToken.None;
        DateTimeOffset plantedAt = DateTimeOffset.UtcNow;

        await realm.PlantAsync(clientId, KioskAttributes, cancellationToken);
        try
        {
            await RunSweepAsync(plantedAt + PastGrace, cancellationToken);

            (await IsEnabledAsync(clientId, cancellationToken)).ShouldBeFalse(
                "a pass past the grace window must disable the orphaned kiosk too (spec 320 §2 US1)");

            using HttpClient munich = await MunichClientAsync();
            HttpResponseMessage response = await EnrollKioskAsync(munich, clientId);
            response.StatusCode.ShouldBe(
                HttpStatusCode.Created,
                $"enrollment must succeed once the orphan is disabled: {await aspire.DiagnoseAsync("identity", response)}");

            JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>();
            string secret = created.GetProperty("clientSecret").GetString()!;

            (await CanAuthenticateAsync(clientId, secret)).ShouldBeTrue(
                "the fresh secret from the healed enrollment must actually work");
        }
        finally
        {
            await realm.DeleteAsync(clientId, cancellationToken);
        }
    }

    [Fact]
    public async Task I2_a_stamped_client_younger_than_the_grace_window_stays_enabled()
    {
        string deviceIdentifier = NewSuffix("too-young");
        string clientId = $"plc-{deviceIdentifier}";
        CancellationToken cancellationToken = CancellationToken.None;
        DateTimeOffset plantedAt = DateTimeOffset.UtcNow;

        await realm.PlantAsync(clientId, DeviceAttributes(deviceIdentifier), cancellationToken);
        try
        {
            await RunSweepAsync(plantedAt + WithinGrace, cancellationToken);

            (await IsEnabledAsync(clientId, cancellationToken)).ShouldBeTrue(
                "a registration still inside the grace window may be a create that has not yet "
                + "committed its row (spec 320 §4.2) and must not be touched");
        }
        finally
        {
            await realm.DeleteAsync(clientId, cancellationToken);
        }
    }

    [Fact]
    public async Task I3_a_device_registered_through_the_endpoint_stays_enabled_after_a_pass_a_day_later()
    {
        string deviceIdentifier = NewSuffix("registered");
        string clientId = $"plc-{deviceIdentifier}";
        CancellationToken cancellationToken = CancellationToken.None;

        using HttpClient munich = await MunichClientAsync();
        HttpResponseMessage registered = await RegisterDeviceAsync(munich, deviceIdentifier);
        registered.StatusCode.ShouldBe(
            HttpStatusCode.Created, await aspire.DiagnoseAsync("identity", registered));

        try
        {
            await RunSweepAsync(DateTimeOffset.UtcNow + ADayLater, cancellationToken);

            (await IsEnabledAsync(clientId, cancellationToken)).ShouldBeTrue(
                "a registered device has an active row and must never be a candidate (S3)");
        }
        finally
        {
            await realm.DeleteAsync(clientId, cancellationToken);
        }
    }

    [Fact]
    public async Task I4_an_unstamped_client_stays_enabled()
    {
        string unstamped = $"unstamped-{NewSuffix("bystander")}";
        CancellationToken cancellationToken = CancellationToken.None;
        DateTimeOffset plantedAt = DateTimeOffset.UtcNow;

        await realm.PlantAsync(unstamped, NoAttributes, cancellationToken);
        try
        {
            await RunSweepAsync(plantedAt + ADayLater, cancellationToken);

            (await IsEnabledAsync(unstamped, cancellationToken)).ShouldBeTrue(
                "a client with no sse.kind at all is never a candidate (spec §4, 'safe by default')");
        }
        finally
        {
            await realm.DeleteAsync(unstamped, cancellationToken);
        }
    }

    [Fact]
    public async Task I5_a_webhook_stamped_client_with_no_row_is_now_disabled()
    {
        string webhook = $"webhook-{NewSuffix("joined-the-sweep")}";
        CancellationToken cancellationToken = CancellationToken.None;
        DateTimeOffset plantedAt = DateTimeOffset.UtcNow;

        await realm.PlantAsync(webhook, WebhookAttributes, cancellationToken);
        try
        {
            await RunSweepAsync(plantedAt + ADayLater, cancellationToken);

            (await IsEnabledAsync(webhook, cancellationToken)).ShouldBeFalse(
                "sse.kind=webhook joined OrphanedClientSweep.SweptKinds (spec 337, #2797) — the same "
                + "RegisteredClient/ClientKind.WebhookIntegration row shape this sweep already queried "
                + "for device/kiosk, so a cancelled RotateWebhookClientCommandHandler create is now "
                + "disabled the same way");
        }
        finally
        {
            await realm.DeleteAsync(webhook, cancellationToken);
        }
    }

    private static string NewSuffix(string label) => $"{label}-{Guid.CreateVersion7():N}";

    private static Dictionary<string, string> DeviceAttributes(string deviceIdentifier) =>
        new(StringComparer.Ordinal)
        {
            ["sse.kind"] = "device",
            ["sse.deviceType"] = "plc",
            ["sse.deviceIdentifier"] = deviceIdentifier,
            ["sse.fab"] = Fab,
        };

    private static readonly Dictionary<string, string> KioskAttributes =
        new(StringComparer.Ordinal) { ["sse.kind"] = "kiosk", ["sse.fab"] = Fab };

    private static readonly Dictionary<string, string> WebhookAttributes =
        new(StringComparer.Ordinal) { ["sse.kind"] = "webhook", ["sse.fab"] = Fab };

    private static readonly Dictionary<string, string> NoAttributes = new(StringComparer.Ordinal);

    private Task<HttpClient> MunichClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "admin@munich.test", SeededCredentials.AdminMunich);

    private static Task<HttpResponseMessage> RegisterDeviceAsync(HttpClient client, string deviceIdentifier) =>
        client.PostAsJsonAsync($"/devices/register?fabId={Fab}", new { deviceType = "plc", deviceIdentifier });

    private static Task<HttpResponseMessage> EnrollKioskAsync(HttpClient client, string clientId) =>
        client.PostAsJsonAsync($"/kiosks/enroll?fabId={Fab}", new { clientId });

    private async Task<bool> IsEnabledAsync(string clientId, CancellationToken cancellationToken)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);
        JsonElement clients = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
            cancellationToken);
        return clients.EnumerateArray().Single().GetProperty("enabled").GetBoolean();
    }

    private async Task<bool> CanAuthenticateAsync(string clientId, string clientSecret)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        HttpResponseMessage token = await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token", form);

        return token.IsSuccessStatusCode;
    }

    /// <summary>
    /// Drives <see cref="OrphanedClientSweep"/> directly, through Identity's
    /// own <c>AddIdentityInfrastructure</c> composed against the fixture's
    /// real Keycloak and real <c>identity-db</c>, with <see cref="IClock"/>
    /// replaced by a fixed clock (last registration wins). The hosted
    /// service's own <c>PeriodicTimer</c> is deliberately not exercised here
    /// — phase 5's procedure (spec §9) is what proves the pass actually runs
    /// at boot.
    /// </summary>
    private async Task<OrphanedClientSweepOutcome> RunSweepAsync(
        DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        string? identityDb = await aspire.App.GetConnectionStringAsync("identity-db", cancellationToken);
        identityDb.ShouldNotBeNullOrEmpty(
            "identity-db must be provisioned by Aspire — without the real connection string this "
            + "test cannot exercise S3's Postgres read at all (plan §8.1).");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Keycloak and identity-db are the real ones behind the fixture.
        // RabbitMQ is read while AddIdentityInfrastructure registers and
        // never dialled — the host is not started, so Wolverine's broker
        // connection is only ever parsed.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:keycloak"] = keycloak.BaseAddress!.ToString(),
            ["ConnectionStrings:identity-db"] = identityDb,
            ["ConnectionStrings:rabbitmq"] = "amqp://unused:unused@127.0.0.1:5672",
            ["Keycloak:Realm"] = RealmProbe.Realm,
            ["Keycloak:AdminClientId"] = RealmProbe.AdminClientId,
            ["Keycloak:AdminClientSecret"] = RealmProbe.AdminClientSecret,
        });

        // Keycloak exposes https only and presents the ASP.NET dev
        // certificate — trusted on a developer machine, not on CI (same
        // reason AspireFixture.CreateKeycloakClient states).
        builder.Services.ConfigureHttpClientDefaults(http =>
            http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            }));

        builder.AddIdentityInfrastructure();

        // Last registration wins: the grace window (S4) is driven by this
        // test's own clock rather than a real 10-minute wait.
        builder.Services.AddSingleton<IClock>(new FixedClock(asOf));

        await using ServiceProvider provider = builder.Services
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        OrphanedClientSweep sweep = scope.ServiceProvider.GetRequiredService<OrphanedClientSweep>();
        return await sweep.SweepAsync(cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
