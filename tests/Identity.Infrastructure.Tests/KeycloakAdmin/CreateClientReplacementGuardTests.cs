using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Phase-6 security review of #2728's first fix (commit <c>2128166f</c>) —
/// <b>deleting any client found by clientId is too wide.</b>
///
/// <para>
/// That fix made <c>CreateClientAsync</c> delete-and-recreate <i>any</i>
/// existing Keycloak client it found by <c>clientId</c>, not just a disabled
/// one from the same registration flow. Kiosk enrolment in particular has no
/// clientId-grammar namespace separating it from the realm's own
/// infrastructure clients (<c>management-web</c>, <c>identity-admin</c>,
/// <c>event-ingestion</c>, …), so a kiosk enrolment naming one of those as
/// its clientId deleted and replaced it — reachable by anyone holding
/// <c>sse.identity.kiosks.write</c>, a default scope on
/// <c>management-web</c> itself.
/// </para>
///
/// <para>
/// <c>CreateClientAsync</c> now only treats a found client as the intended
/// replacement case — a previously disabled registration — when it is
/// disabled <i>and</i> stamped with the same <c>sse.kind</c> (and
/// <c>sse.fab</c>, where both sides have one) as the client being created.
/// Everything else, including an active client and a disabled client of a
/// different kind, is a genuine conflict.
/// </para>
/// </summary>
public class CreateClientReplacementGuardTests
{
    private const string Realm = "smart-sentinel-eye";
    private const string ExistingClientUuid = "11111111-1111-1111-1111-111111111111";

    /// <summary>
    /// Blocker 1 / 2 from the review: an active client under this clientId —
    /// whether it is a genuine double-registration race or another fab's live
    /// webhook client sharing the name — must still conflict rather than be
    /// deleted out from under its owner.
    /// </summary>
    [Fact]
    public async Task An_active_client_with_the_same_clientId_still_conflicts()
    {
        const string ClientId = "webhook-acme";
        StubKeycloakHandler keycloak = new(ActiveSameKindClientFlow(ClientId));

        KeycloakClientAlreadyExistsException thrown = await Should.ThrowAsync<KeycloakClientAlreadyExistsException>(
            () => CreateAsync(keycloak, Representation(ClientId, kind: "webhook", fab: "fab-b")));

        thrown.ClientId.ShouldBe(ClientId);
        keycloak.Requests.ShouldNotContain(
            request => request.Method == "DELETE",
            "the found client is active (not disabled), so it must never be a delete candidate "
            + "regardless of how it was named");
    }

    /// <summary>
    /// Blocker 1 itself: a realm infrastructure client such as
    /// <c>management-web</c> carries no <c>sse.kind</c> attribute at all,
    /// because nothing of ours ever stamped it. A kiosk enrolment naming that
    /// clientId must not delete it — even granting the attacker's premise
    /// that it is somehow disabled, which makes this the hardest case to
    /// refuse, not an easy one.
    /// </summary>
    [Fact]
    public async Task A_client_with_no_sse_kind_attribute_is_never_deleted_even_if_disabled()
    {
        const string ClientId = "management-web";
        StubKeycloakHandler keycloak = new(DisabledNoKindAttributeFlow(ClientId));

        KeycloakClientAlreadyExistsException thrown = await Should.ThrowAsync<KeycloakClientAlreadyExistsException>(
            () => CreateAsync(keycloak, Representation(ClientId, kind: "kiosk", fab: "munich")));

        thrown.ClientId.ShouldBe(ClientId);
        keycloak.Requests.ShouldNotContain(
            request => request.Method == "DELETE",
            "a realm infrastructure client carries no sse.kind stamp of our own, so it must never "
            + "be deleted no matter what clientId a kiosk enrolment names or what `enabled` reads");
    }

    /// <summary>
    /// A disabled client of a <i>different</i> kind than the one being
    /// created — the most realistic cross-kind case: a kiosk enrolment
    /// naming a disabled device's clientId — must not be treated as that
    /// device's replacement. Only a same-kind disabled registration is.
    /// </summary>
    [Fact]
    public async Task A_disabled_client_of_a_different_kind_is_not_deleted()
    {
        const string ClientId = "plc-oven-12";
        StubKeycloakHandler keycloak = new(DisabledDifferentKindFlow(ClientId));

        KeycloakClientAlreadyExistsException thrown = await Should.ThrowAsync<KeycloakClientAlreadyExistsException>(
            () => CreateAsync(keycloak, Representation(ClientId, kind: "kiosk", fab: "munich")));

        thrown.ClientId.ShouldBe(ClientId);
        keycloak.Requests.ShouldNotContain(
            request => request.Method == "DELETE",
            "the found client is disabled, but it is a device registration, not a kiosk one — "
            + "a different kind under the same clientId is a conflict, not a replacement");
    }

    private static Task<KeycloakClientCredentials> CreateAsync(
        StubKeycloakHandler keycloak, KeycloakClientRepresentation representation) =>
        new HttpKeycloakAdminClient(
                new HttpClient(keycloak) { BaseAddress = new Uri("http://keycloak.test/") },
                Options.Create(new KeycloakAdminOptions { Realm = Realm }),
                NullLogger<HttpKeycloakAdminClient>.Instance)
            .CreateClientAsync(representation, "/fabs/munich", CancellationToken.None);

    private static KeycloakClientRepresentation Representation(string clientId, string kind, string fab) =>
        new(ClientId: clientId,
            Name: clientId,
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes: ["sse.streams.view"],
            OptionalClientScopes: [],
            Attributes: new Dictionary<string, string> { ["sse.kind"] = kind, ["sse.fab"] = fab });

    private static Func<StubKeycloakHandler, RecordedRequest, string?> ActiveSameKindClientFlow(string clientId) =>
        (_, request) => request switch
        {
            { Method: "GET", PathAndQuery: var path } when path.Contains("clientId=", StringComparison.Ordinal) =>
                $$$"""[{"id":"{{{ExistingClientUuid}}}","clientId":"{{{clientId}}}","enabled":true,"attributes":{"sse.kind":"webhook","sse.fab":"fab-a"}}]""",
            _ => null,
        };

    private static Func<StubKeycloakHandler, RecordedRequest, string?> DisabledNoKindAttributeFlow(string clientId) =>
        (_, request) => request switch
        {
            { Method: "GET", PathAndQuery: var path } when path.Contains("clientId=", StringComparison.Ordinal) =>
                $$$"""[{"id":"{{{ExistingClientUuid}}}","clientId":"{{{clientId}}}","enabled":false,"attributes":{}}]""",
            _ => null,
        };

    private static Func<StubKeycloakHandler, RecordedRequest, string?> DisabledDifferentKindFlow(string clientId) =>
        (_, request) => request switch
        {
            { Method: "GET", PathAndQuery: var path } when path.Contains("clientId=", StringComparison.Ordinal) =>
                $$$"""[{"id":"{{{ExistingClientUuid}}}","clientId":"{{{clientId}}}","enabled":false,"attributes":{"sse.kind":"device","sse.fab":"munich"}}]""",
            _ => null,
        };
}
