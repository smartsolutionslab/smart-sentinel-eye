using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Issue #2728 — <b>disabling a device may permanently block re-registering it.</b>
///
/// <para>
/// <c>DisableClientAsync</c> only flips <c>enabled</c> to <c>false</c> on the
/// Keycloak client; it never deletes it. <c>TryGetClientUuidAsync</c> queries by
/// <c>clientId</c> with no <c>enabled</c> filter, and <c>CreateClientAsync</c>'s
/// existence probe reuses that same lookup — so a disabled client is still found,
/// and re-registration throws <see cref="KeycloakClientAlreadyExistsException"/>
/// exactly as a genuine conflict would, even though
/// <c>RegisteredClientRepository.GetByClientIdAsync</c> already released the
/// <c>clientId</c> on the database side.
/// </para>
///
/// <para>
/// Driven through the real client at the transport seam (mirrors
/// <c>HalfEnrolledClientCompensationTests</c> / <c>EnrolledKioskQueryTests</c>):
/// the defect is in <c>CreateClientAsync</c>'s existence probe, which a double
/// standing in for <see cref="IKeycloakAdminClient"/> cannot see.
/// </para>
/// </summary>
public class DisabledClientReregistrationTests
{
    private const string Realm = "smart-sentinel-eye";
    private const string ClientId = "plc-oven-12";
    private const string DisabledClientUuid = "11111111-1111-1111-1111-111111111111";
    private const string FreshClientUuid = "22222222-2222-2222-2222-222222222222";

    /// <summary>
    /// A disabled client that carries no <c>sse.kind</c> attribute is not the
    /// replacement case this issue describes — none of our own registrations
    /// leave a disabled client without one, so this is some other disabled
    /// client Keycloak happens to carry under the same clientId — and the
    /// probe must still refuse rather than delete it.
    /// </summary>
    [Fact]
    public async Task A_disabled_client_without_an_sse_kind_attribute_still_conflicts()
    {
        StubKeycloakHandler keycloak = new(DisabledClientStillPresentFlow);

        KeycloakClientAlreadyExistsException thrown = await Should.ThrowAsync<KeycloakClientAlreadyExistsException>(
            () => CreateAsync(keycloak));

        thrown.ClientId.ShouldBe(
            ClientId,
            "the found client is disabled but carries no sse.kind stamp of our own, so it is not "
            + "eligible for replacement and the probe must treat it as a genuine conflict");
    }

    /// <summary>
    /// The desired behaviour (decision recorded on #2728): finding a disabled
    /// client on re-registration deletes it first, then creates fresh, so
    /// re-registering a previously-disabled device succeeds instead of 409ing.
    /// </summary>
    [Fact]
    public async Task Re_registering_a_previously_disabled_clientId_succeeds()
    {
        StubKeycloakHandler keycloak = new(DisabledClientIsReplacedFlow);

        KeycloakClientCredentials credentials = await CreateAsync(keycloak);

        credentials.ClientSecret.ShouldBe(
            "fresh-secret-after-reregistration",
            "the disabled client should have been deleted and a fresh one created in its place");
    }

    private static Task<KeycloakClientCredentials> CreateAsync(StubKeycloakHandler keycloak) =>
        new HttpKeycloakAdminClient(
                new HttpClient(keycloak) { BaseAddress = new Uri("http://keycloak.test/") },
                Options.Create(new KeycloakAdminOptions { Realm = Realm }),
                NullLogger<HttpKeycloakAdminClient>.Instance)
            .CreateClientAsync(Representation(), "/fabs/munich", CancellationToken.None);

    private static KeycloakClientRepresentation Representation() =>
        new(ClientId: ClientId,
            Name: "plc oven-12",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes: ["sse.streams.view"],
            OptionalClientScopes: [],
            Attributes: new Dictionary<string, string> { ["sse.kind"] = "device" });

    /// <summary>
    /// Keycloak holding a disabled client under this clientId, but one with no
    /// <c>sse.kind</c> attribute of our own — so the probe throws before it
    /// ever attempts a delete, and nothing past the first request matters.
    /// </summary>
    private static string? DisabledClientStillPresentFlow(StubKeycloakHandler keycloak, RecordedRequest request) =>
        request switch
        {
            { Method: "GET", PathAndQuery: var path } when path.Contains("clientId=", StringComparison.Ordinal) =>
                $$"""[{"id":"{{DisabledClientUuid}}","clientId":"{{ClientId}}","enabled":false}]""",
            _ => null,
        };

    /// <summary>
    /// The full flow the fixed implementation needs: the first probe finds the
    /// disabled client stamped with the same <c>sse.kind</c> as the one being
    /// (re-)created, a delete removes it, the create proceeds, and the second
    /// probe (after create) finds the freshly-minted one.
    /// </summary>
    private static string? DisabledClientIsReplacedFlow(StubKeycloakHandler keycloak, RecordedRequest request) =>
        request switch
        {
            { Method: "GET", PathAndQuery: var path } when path.Contains("clientId=", StringComparison.Ordinal) =>
                keycloak.CountOf("clientId=") == 1
                    ? $$$"""[{"id":"{{{DisabledClientUuid}}}","clientId":"{{{ClientId}}}","enabled":false,"attributes":{"sse.kind":"device"}}]"""
                    : $$"""[{"id":"{{FreshClientUuid}}","clientId":"{{ClientId}}"}]""",
            { Method: "GET", PathAndQuery: var path } when path.EndsWith("/service-account-user", StringComparison.Ordinal) =>
                """{"id":"33333333-3333-3333-3333-333333333333"}""",
            { Method: "GET", PathAndQuery: var path } when path.Contains("/group-by-path/", StringComparison.Ordinal) =>
                """{"id":"44444444-4444-4444-4444-444444444444","name":"munich","path":"/fabs/munich"}""",
            { Method: "GET", PathAndQuery: var path } when path.EndsWith("/role-mappings/realm", StringComparison.Ordinal) =>
                "[]",
            { Method: "GET", PathAndQuery: var path } when path.EndsWith("/client-secret", StringComparison.Ordinal) =>
                """{"type":"secret","value":"fresh-secret-after-reregistration"}""",
            _ => null,
        };
}
