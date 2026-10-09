using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 320 (#2181) plan §8.3/§3 — <c>HttpKeycloakAdminClient.GetServiceAccountCreatedAtAsync</c>,
/// the read <c>OrphanedClientSweep</c> uses to tell a dead registration from
/// one still in flight (the grace window, S4). <c>createdTimestamp</c> is the
/// only creation time Keycloak records for a client at all — it belongs to
/// the client's service-account <b>user</b>, not the client representation
/// itself.
/// </summary>
public class ServiceAccountCreatedAtTests
{
    private const string Realm = "smart-sentinel-eye";
    private const string ClientUuid = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task A_service_accounts_createdTimestamp_is_mapped_to_a_DateTimeOffset()
    {
        const long epochMilliseconds = 1_760_000_000_000;
        StubKeycloakHandler keycloak = new(
            Flow($$"""{"id":"sa-1","createdTimestamp":{{epochMilliseconds}}}"""));

        Option<DateTimeOffset> createdAt = await ClientOver(keycloak)
            .GetServiceAccountCreatedAtAsync("plc-a", CancellationToken.None);

        createdAt.HasValue.ShouldBeTrue();
        createdAt.Value.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds));
    }

    [Fact]
    public async Task A_client_keycloak_does_not_have_answers_none()
    {
        // The clientId= existence probe finds nothing — exactly what a
        // client deleted (or never created) looks like.
        StubKeycloakHandler keycloak = new((_, _) => "[]");

        Option<DateTimeOffset> createdAt = await ClientOver(keycloak)
            .GetServiceAccountCreatedAtAsync("plc-missing", CancellationToken.None);

        createdAt.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task A_404_on_the_service_account_user_answers_none()
    {
        StubKeycloakHandler keycloak = new(Flow(serviceAccountBody: null))
        {
            Refuse = request => IsServiceAccountUserRequest(request) ? HttpStatusCode.NotFound : null,
        };

        Option<DateTimeOffset> createdAt = await ClientOver(keycloak)
            .GetServiceAccountCreatedAtAsync("plc-a", CancellationToken.None);

        createdAt.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task A_null_createdTimestamp_answers_none()
    {
        // Measured against the pinned Keycloak 26.6.4 by the integration
        // control I0b (spec 320 plan §9, risk table) — this unit covers the
        // defensive case should a future version ever omit the field.
        StubKeycloakHandler keycloak = new(Flow("""{"id":"sa-1"}"""));

        Option<DateTimeOffset> createdAt = await ClientOver(keycloak)
            .GetServiceAccountCreatedAtAsync("plc-a", CancellationToken.None);

        createdAt.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task A_server_error_on_the_service_account_user_throws()
    {
        StubKeycloakHandler keycloak = new(Flow(serviceAccountBody: null))
        {
            Refuse = request => IsServiceAccountUserRequest(request) ? HttpStatusCode.InternalServerError : null,
        };

        await Should.ThrowAsync<HttpRequestException>(
            () => ClientOver(keycloak).GetServiceAccountCreatedAtAsync("plc-a", CancellationToken.None));
    }

    private static bool IsServiceAccountUserRequest(RecordedRequest request) =>
        request.PathAndQuery.EndsWith("/service-account-user", StringComparison.Ordinal);

    private static Func<StubKeycloakHandler, RecordedRequest, string?> Flow(string? serviceAccountBody) =>
        (_, request) => request switch
        {
            { PathAndQuery: var path } when path.Contains("clientId=", StringComparison.Ordinal) =>
                $$"""[{"id":"{{ClientUuid}}","clientId":"plc-a"}]""",
            _ when IsServiceAccountUserRequest(request) => serviceAccountBody,
            _ => null,
        };

    private static HttpKeycloakAdminClient ClientOver(StubKeycloakHandler keycloak) =>
        new(new HttpClient(keycloak) { BaseAddress = new Uri("http://keycloak.test/") },
            Options.Create(new KeycloakAdminOptions { Realm = Realm }),
            NullLogger<HttpKeycloakAdminClient>.Instance);
}
