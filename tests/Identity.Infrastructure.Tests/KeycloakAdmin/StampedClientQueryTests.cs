using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 320 (#2181) plan §8.3 — <c>HttpKeycloakAdminClient.GetStampedClientsAsync</c>,
/// the one <c>GET /clients</c> <c>OrphanedClientSweep</c> reads before Postgres
/// (S5). Mirrors <see cref="EnrolledKioskQueryTests"/>: a stub
/// <see cref="HttpMessageHandler"/> rather than <c>FakeKeycloakAdminClient</c>,
/// because the defect this guards against lives inside this class's own
/// deserialisation, not behind the port (#2207's reasoning).
/// </summary>
public class StampedClientQueryTests
{
    private const string Realm = "smart-sentinel-eye";

    /// <summary>
    /// Four rows a realm really has together: a device, a kiosk (disabled —
    /// #2728's replace population leaves exactly this shape), a webhook
    /// integration (out of <c>OrphanedClientSweep.SweptKinds</c>, but still a
    /// stamped client this adapter must report — the caller filters, not the
    /// adapter), and a realm-infrastructure client with no <c>sse.kind</c> at
    /// all.
    /// </summary>
    private const string AllClients = """
        [
          {"id":"11111111-1111-1111-1111-111111111111","clientId":"plc-device-a","enabled":true,
           "attributes":{"sse.kind":"device"}},
          {"id":"22222222-2222-2222-2222-222222222222","clientId":"kiosk-b","enabled":false,
           "attributes":{"sse.kind":"kiosk"}},
          {"id":"33333333-3333-3333-3333-333333333333","clientId":"webhook-c","enabled":true,
           "attributes":{"sse.kind":"webhook"}},
          {"id":"44444444-4444-4444-4444-444444444444","clientId":"realm-management","enabled":true}
        ]
        """;

    [Fact]
    public async Task Only_clients_carrying_an_sse_kind_attribute_are_reported_with_their_kind_and_enabled_flag()
    {
        StubKeycloakHandler keycloak = new((_, _) => AllClients);

        IReadOnlyList<StampedClient> stamped = await ClientOver(keycloak)
            .GetStampedClientsAsync(CancellationToken.None);

        stamped.ShouldBe(
            [
                new StampedClient("plc-device-a", "device", Enabled: true),
                new StampedClient("kiosk-b", "kiosk", Enabled: false),
                new StampedClient("webhook-c", "webhook", Enabled: true),
            ],
            ignoreOrder: true,
            "the caller (OrphanedClientSweep.SweptKinds) decides which kinds it acts on; this "
            + "adapter's only filter is whether sse.kind is present at all, which is what excludes "
            + "realm-management");
    }

    [Fact]
    public async Task A_failed_listing_throws_rather_than_reporting_an_empty_realm()
    {
        StubKeycloakHandler keycloak = new((_, _) => null)
        {
            Refuse = _ => System.Net.HttpStatusCode.InternalServerError,
        };

        await Should.ThrowAsync<HttpRequestException>(
            () => ClientOver(keycloak).GetStampedClientsAsync(CancellationToken.None));
    }

    private static HttpKeycloakAdminClient ClientOver(StubKeycloakHandler keycloak) =>
        new(new HttpClient(keycloak) { BaseAddress = new Uri("http://keycloak.test/") },
            Options.Create(new KeycloakAdminOptions { Realm = Realm }),
            NullLogger<HttpKeycloakAdminClient>.Instance);
}
