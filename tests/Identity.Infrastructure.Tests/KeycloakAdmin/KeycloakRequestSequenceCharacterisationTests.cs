using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Characterisation (spec 348, ADR-0144), captured green on the unmodified
/// <c>HttpKeycloakAdminClient</c> before <c>CreateClientAsync</c> and
/// <c>GetSubGroupNamesAsync</c> are split into private helpers and the class
/// into <c>partial</c> files. The client creates, deletes and re-keys live
/// Keycloak credentials, so the contract that matters is the complete ordered
/// request list per scenario (verb, path and query, body), not the outcome:
/// the existence probe, the replace-only-a-disabled-client path, the group
/// join, the strip, the secret read and the compensating delete that covers
/// exactly those last three and rethrows. Existing tests assert pieces of
/// this with <c>ShouldContain</c>; none can see an order.
///
/// <para>
/// Counterfactual this file was seen to catch: moving the existence probe's
/// delete-and-replace after the <c>POST</c> turns
/// <see cref="A_disabled_client_of_the_same_kind_and_fab_is_deleted_before_the_post"/> red;
/// stopping <c>GetSubGroupNamesAsync</c> after its first page turns
/// <see cref="Sub_groups_are_read_page_by_page_until_a_short_page"/> red.
/// </para>
/// </summary>
public class KeycloakRequestSequenceCharacterisationTests
{
    private const string Realm = "smart-sentinel-eye";
    private const string Base = "/admin/realms/smart-sentinel-eye";
    private const string OldUuid = "11111111-1111-1111-1111-111111111111";
    private const string NewUuid = "22222222-2222-2222-2222-222222222222";
    private const string UserUuid = "33333333-3333-3333-3333-333333333333";
    private const string GroupUuid = "44444444-4444-4444-4444-444444444444";
    private const string ClientId = "kiosk.munich.press-1";

    private const string RolesJson = """[{"id":"role-1","name":"offline_access"}]""";

    private const string PostedBody =
        """{"clientId":"kiosk.munich.press-1","name":"Kiosk kiosk.munich.press-1","serviceAccountsEnabled":true,"standardFlowEnabled":false,"directAccessGrantsEnabled":false,"publicClient":false,"defaultClientScopes":["sse.cameras.read","sse-audience"],"optionalClientScopes":[],"attributes":{"sse.kind":"kiosk","sse.fab":"munich"}}""";

    private static RecordedRequest Req(string method, string path, string body = "") =>
        new(method, Base + path, body);

    private static RecordedRequest Probe(string clientId = ClientId) =>
        Req("GET", $"/clients?clientId={clientId}");

    private static RecordedRequest[] FreshCreateRequests() =>
    [
        Probe(),
        Req("POST", "/clients", PostedBody),
        Probe(),
        Req("GET", $"/clients/{NewUuid}/service-account-user"),
        Req("GET", "/group-by-path/fabs/munich"),
        Req("PUT", $"/users/{UserUuid}/groups/{GroupUuid}"),
        Req("GET", $"/clients/{NewUuid}/service-account-user"),
        Req("GET", $"/users/{UserUuid}/role-mappings/realm"),
        Req("DELETE", $"/users/{UserUuid}/role-mappings/realm", RolesJson),
        Req("GET", $"/clients/{NewUuid}/client-secret"),
    ];

    private static KeycloakClientRepresentation Representation(
        string kind = "kiosk",
        string fab = "munich") =>
        new(ClientId: ClientId,
            Name: $"Kiosk {ClientId}",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes: ["sse.cameras.read", "sse-audience"],
            OptionalClientScopes: [],
            Attributes: new Dictionary<string, string> { ["sse.kind"] = kind, ["sse.fab"] = fab });

    private static string ExistingRow(bool enabled, string kind, string fab) =>
        $$$"""[{"id":"{{{OldUuid}}}","clientId":"{{{ClientId}}}","enabled":{{{(enabled ? "true" : "false")}}},"attributes":{"sse.kind":"{{{kind}}}","sse.fab":"{{{fab}}}"}}]""";

    /// <summary>
    /// A realm that answers every call a create makes. The first existence
    /// probe answers <paramref name="firstProbe"/> (no client by default); a
    /// second one answers the freshly created client.
    /// </summary>
    private static Func<StubKeycloakHandler, RecordedRequest, string?> RealmFlow(
        string firstProbe = "[]",
        string roles = RolesJson,
        bool hideNewClient = false) =>
        (keycloak, request) =>
        {
            var path = request.PathAndQuery;
            if (request.Method != "GET")
            {
                return null;
            }

            if (path.Contains("clientId=", StringComparison.Ordinal))
            {
                if (keycloak.CountOf("clientId=") == 1)
                {
                    return firstProbe;
                }

                return hideNewClient
                    ? "[]"
                    : $$$"""[{"id":"{{{NewUuid}}}","clientId":"{{{ClientId}}}","enabled":true}]""";
            }

            if (path.EndsWith("/service-account-user", StringComparison.Ordinal))
            {
                return $$$"""{"id":"{{{UserUuid}}}"}""";
            }

            if (path.Contains("/group-by-path/", StringComparison.Ordinal))
            {
                return $$$"""{"id":"{{{GroupUuid}}}","name":"munich","path":"/fabs/munich"}""";
            }

            if (path.EndsWith("/role-mappings/realm", StringComparison.Ordinal))
            {
                return roles;
            }

            return path.EndsWith("/client-secret", StringComparison.Ordinal)
                ? """{"type":"secret","value":"fake-fresh-secret"}"""
                : null;
        };

    private static HttpKeycloakAdminClient ClientOver(StubKeycloakHandler keycloak) =>
        new(new HttpClient(keycloak) { BaseAddress = new Uri("http://keycloak.test/") },
            Options.Create(new KeycloakAdminOptions { Realm = Realm }),
            NullLogger<HttpKeycloakAdminClient>.Instance);

    private static Task<KeycloakClientCredentials> CreateAsync(
        StubKeycloakHandler keycloak,
        KeycloakClientRepresentation? representation = null) =>
        ClientOver(keycloak).CreateClientAsync(
            representation ?? Representation(), "/fabs/munich", CancellationToken.None);

    // ---- CreateClientAsync ------------------------------------------------------

    [Fact]
    public async Task A_fresh_create_sends_exactly_these_requests_in_this_order()
    {
        StubKeycloakHandler keycloak = new(RealmFlow());

        var credentials = await CreateAsync(keycloak);

        credentials.ClientSecret.ShouldBe("fake-fresh-secret");
        keycloak.Requests.ShouldBe(FreshCreateRequests());
    }

    [Fact]
    public async Task A_create_for_an_already_stripped_account_sends_no_role_removal()
    {
        StubKeycloakHandler keycloak = new(RealmFlow(roles: "[]"));

        await CreateAsync(keycloak);

        keycloak.Requests.ShouldBe(
            [
                Probe(),
                Req("POST", "/clients", PostedBody),
                Probe(),
                Req("GET", $"/clients/{NewUuid}/service-account-user"),
                Req("GET", "/group-by-path/fabs/munich"),
                Req("PUT", $"/users/{UserUuid}/groups/{GroupUuid}"),
                Req("GET", $"/clients/{NewUuid}/service-account-user"),
                Req("GET", $"/users/{UserUuid}/role-mappings/realm"),
                Req("GET", $"/clients/{NewUuid}/client-secret"),
            ]);
    }

    [Fact]
    public async Task A_disabled_client_of_the_same_kind_and_fab_is_deleted_before_the_post()
    {
        StubKeycloakHandler keycloak = new(RealmFlow(firstProbe: ExistingRow(enabled: false, "kiosk", "munich")));

        var credentials = await CreateAsync(keycloak);

        credentials.ClientSecret.ShouldBe("fake-fresh-secret");
        keycloak.Requests.ShouldBe(
            [
                Probe(),
                Req("DELETE", $"/clients/{OldUuid}"),
                .. FreshCreateRequests()[1..],
            ]);
    }

    [Fact]
    public async Task An_enabled_client_under_the_same_client_id_stops_the_create_after_the_probe()
    {
        StubKeycloakHandler keycloak = new(RealmFlow(firstProbe: ExistingRow(enabled: true, "kiosk", "munich")));

        var thrown = await Should.ThrowAsync<KeycloakClientAlreadyExistsException>(() => CreateAsync(keycloak));

        thrown.ClientId.ShouldBe(ClientId);
        thrown.Message.ShouldBe("Keycloak client 'kiosk.munich.press-1' already exists.");
        keycloak.Requests.ShouldBe([Probe()]);
    }

    [Fact]
    public async Task A_disabled_client_of_another_fab_stops_the_create_after_the_probe()
    {
        StubKeycloakHandler keycloak = new(RealmFlow(firstProbe: ExistingRow(enabled: false, "kiosk", "dresden")));

        await Should.ThrowAsync<KeycloakClientAlreadyExistsException>(() => CreateAsync(keycloak));

        keycloak.Requests.ShouldBe([Probe()]);
    }

    [Fact]
    public async Task A_refused_group_join_is_compensated_by_a_last_delete_and_the_original_failure_is_rethrown()
    {
        StubKeycloakHandler keycloak = new(RealmFlow())
        {
            Refuse = request => request.Method == "PUT" ? HttpStatusCode.Forbidden : null,
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => CreateAsync(keycloak));

        thrown.Message.ShouldBe("Response status code does not indicate success: 403 (Forbidden).");
        keycloak.Requests.ShouldBe(
            [
                .. FreshCreateRequests()[..6],
                Req("DELETE", $"/clients/{NewUuid}"),
            ]);
    }

    [Fact]
    public async Task A_refused_role_listing_is_compensated_by_a_last_delete_and_the_original_failure_is_rethrown()
    {
        StubKeycloakHandler keycloak = new(RealmFlow())
        {
            Refuse = request => request.PathAndQuery.EndsWith("/role-mappings/realm", StringComparison.Ordinal)
                && request.Method == "GET" ? HttpStatusCode.InternalServerError : null,
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => CreateAsync(keycloak));

        thrown.Message.ShouldBe("Response status code does not indicate success: 500 (Internal Server Error).");
        keycloak.Requests.ShouldBe(
            [
                .. FreshCreateRequests()[..8],
                Req("DELETE", $"/clients/{NewUuid}"),
            ]);
    }

    [Fact]
    public async Task A_refused_role_removal_is_compensated_by_a_last_delete_and_the_original_failure_is_rethrown()
    {
        StubKeycloakHandler keycloak = new(RealmFlow())
        {
            Refuse = request => request.Method == "DELETE"
                && request.PathAndQuery.EndsWith("/role-mappings/realm", StringComparison.Ordinal)
                ? HttpStatusCode.Forbidden : null,
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => CreateAsync(keycloak));

        thrown.Message.ShouldBe("Response status code does not indicate success: 403 (Forbidden).");
        keycloak.Requests.ShouldBe(
            [
                .. FreshCreateRequests()[..9],
                Req("DELETE", $"/clients/{NewUuid}"),
            ]);
    }

    [Fact]
    public async Task A_refused_secret_read_is_compensated_by_a_last_delete_and_the_original_failure_is_rethrown()
    {
        StubKeycloakHandler keycloak = new(RealmFlow())
        {
            Refuse = request => request.PathAndQuery.EndsWith("/client-secret", StringComparison.Ordinal)
                ? HttpStatusCode.BadGateway : null,
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => CreateAsync(keycloak));

        thrown.Message.ShouldBe("Response status code does not indicate success: 502 (Bad Gateway).");
        keycloak.Requests.ShouldBe(
            [
                .. FreshCreateRequests(),
                Req("DELETE", $"/clients/{NewUuid}"),
            ]);
    }

    [Fact]
    public async Task A_compensating_delete_that_is_itself_refused_still_rethrows_the_original_failure()
    {
        StubKeycloakHandler keycloak = new(RealmFlow())
        {
            Refuse = request => request.Method switch
            {
                "PUT" => HttpStatusCode.Forbidden,
                "DELETE" => HttpStatusCode.InternalServerError,
                _ => null,
            },
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => CreateAsync(keycloak));

        thrown.Message.ShouldBe("Response status code does not indicate success: 403 (Forbidden).");
        keycloak.Requests[^1].ShouldBe(Req("DELETE", $"/clients/{NewUuid}"));
    }

    [Fact]
    public async Task A_cancellation_after_the_post_is_not_compensated()
    {
        StubKeycloakHandler keycloak = new((handler, request) =>
            request.Method == "PUT"
                ? throw new OperationCanceledException("cancelled at the group join")
                : RealmFlow()(handler, request));

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() => CreateAsync(keycloak));

        thrown.ShouldNotBeNull();
        keycloak.Requests.ShouldBe(FreshCreateRequests()[..6]);
    }

    [Fact]
    public async Task A_refused_post_is_not_compensated_because_nothing_was_created()
    {
        StubKeycloakHandler keycloak = new(RealmFlow())
        {
            Refuse = request => request.Method == "POST" ? HttpStatusCode.InternalServerError : null,
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => CreateAsync(keycloak));

        thrown.Message.ShouldBe("Response status code does not indicate success: 500 (Internal Server Error).");
        keycloak.Requests.ShouldBe([Probe(), Req("POST", "/clients", PostedBody)]);
    }

    [Fact]
    public async Task A_post_answered_409_is_a_name_conflict_with_no_further_request()
    {
        StubKeycloakHandler keycloak = new(RealmFlow())
        {
            Refuse = request => request.Method == "POST" ? HttpStatusCode.Conflict : null,
        };

        var thrown = await Should.ThrowAsync<KeycloakClientAlreadyExistsException>(() => CreateAsync(keycloak));

        thrown.ClientId.ShouldBe(ClientId);
        keycloak.Requests.ShouldBe([Probe(), Req("POST", "/clients", PostedBody)]);
    }

    [Fact]
    public async Task A_created_client_that_is_not_visible_to_the_second_probe_fails_without_a_delete()
    {
        StubKeycloakHandler keycloak = new(RealmFlow(hideNewClient: true));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => CreateAsync(keycloak));

        thrown.Message.ShouldBe(
            "Keycloak accepted POST /clients but no client with clientId='kiosk.munich.press-1' is visible.");
        keycloak.Requests.ShouldBe([Probe(), Req("POST", "/clients", PostedBody), Probe()]);
    }

    [Fact]
    public async Task A_client_id_is_escaped_in_the_probe_query()
    {
        StubKeycloakHandler keycloak = new((_, _) => "[]")
        {
            Refuse = request => request.Method == "POST" ? HttpStatusCode.Conflict : null,
        };
        var representation = Representation() with { ClientId = "kiosk a+b" };

        await Should.ThrowAsync<KeycloakClientAlreadyExistsException>(() => CreateAsync(keycloak, representation));

        keycloak.Requests[0].ShouldBe(Req("GET", "/clients?clientId=kiosk%20a%2Bb"));
    }

    // ---- secrets ---------------------------------------------------------------

    private static Func<StubKeycloakHandler, RecordedRequest, string?> KnownClient(string secretJson) =>
        (_, request) =>
            request.PathAndQuery.Contains("clientId=", StringComparison.Ordinal)
                ? $$$"""[{"id":"{{{NewUuid}}}","clientId":"webhook-qa","enabled":true}]"""
                : secretJson;

    [Fact]
    public async Task Rotating_a_secret_probes_then_posts_an_empty_body_to_the_client_secret_path()
    {
        StubKeycloakHandler keycloak = new(KnownClient("""{"type":"secret","value":"fake-rotated-secret"}"""));

        var credentials = await ClientOver(keycloak).RotateClientSecretAsync("webhook-qa", CancellationToken.None);

        credentials.ClientSecret.ShouldBe("fake-rotated-secret");
        keycloak.Requests.ShouldBe(
            [
                Req("GET", "/clients?clientId=webhook-qa"),
                Req("POST", $"/clients/{NewUuid}/client-secret"),
            ]);
    }

    [Fact]
    public async Task Rotating_the_secret_of_an_unknown_client_stops_after_the_probe()
    {
        StubKeycloakHandler keycloak = new((_, _) => "[]");

        var thrown = await Should.ThrowAsync<KeycloakClientNotFoundException>(
            () => ClientOver(keycloak).RotateClientSecretAsync("webhook-qa", CancellationToken.None));

        thrown.ClientId.ShouldBe("webhook-qa");
        thrown.Message.ShouldBe("Keycloak client 'webhook-qa' not found.");
        keycloak.Requests.ShouldBe([Req("GET", "/clients?clientId=webhook-qa")]);
    }

    [Fact]
    public async Task An_empty_rotate_response_is_an_invalid_operation_naming_the_client()
    {
        StubKeycloakHandler keycloak = new(KnownClient("null"));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => ClientOver(keycloak).RotateClientSecretAsync("webhook-qa", CancellationToken.None));

        thrown.Message.ShouldBe("Keycloak returned an empty rotate-secret response for clientId='webhook-qa'.");
    }

    [Fact]
    public async Task Reading_a_secret_probes_then_gets_the_client_secret_path_and_never_posts()
    {
        StubKeycloakHandler keycloak = new(KnownClient("""{"type":"secret","value":"fake-existing-secret"}"""));

        var credentials = await ClientOver(keycloak).ReadClientSecretAsync("webhook-qa", CancellationToken.None);

        credentials.ClientSecret.ShouldBe("fake-existing-secret");
        keycloak.Requests.ShouldBe(
            [
                Req("GET", "/clients?clientId=webhook-qa"),
                Req("GET", $"/clients/{NewUuid}/client-secret"),
            ]);
    }

    [Fact]
    public async Task Reading_the_secret_of_an_unknown_client_stops_after_the_probe()
    {
        StubKeycloakHandler keycloak = new((_, _) => "[]");

        var thrown = await Should.ThrowAsync<KeycloakClientNotFoundException>(
            () => ClientOver(keycloak).ReadClientSecretAsync("webhook-qa", CancellationToken.None));

        thrown.Message.ShouldBe("Keycloak client 'webhook-qa' not found.");
        keycloak.Requests.ShouldBe([Req("GET", "/clients?clientId=webhook-qa")]);
    }

    [Fact]
    public async Task An_empty_client_secret_response_is_an_invalid_operation()
    {
        StubKeycloakHandler keycloak = new(KnownClient("null"));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => ClientOver(keycloak).ReadClientSecretAsync("webhook-qa", CancellationToken.None));

        thrown.Message.ShouldBe("Keycloak returned an empty client-secret response.");
    }

    // ---- disable ----------------------------------------------------------------

    [Fact]
    public async Task Disabling_a_known_client_probes_then_puts_enabled_false()
    {
        StubKeycloakHandler keycloak = new(KnownClient("{}"));

        await ClientOver(keycloak).DisableClientAsync("webhook-qa", CancellationToken.None);

        keycloak.Requests.ShouldBe(
            [
                Req("GET", "/clients?clientId=webhook-qa"),
                Req("PUT", $"/clients/{NewUuid}", """{"enabled":false}"""),
            ]);
    }

    [Fact]
    public async Task Disabling_an_unknown_client_sends_no_put()
    {
        StubKeycloakHandler keycloak = new((_, _) => "[]");

        await ClientOver(keycloak).DisableClientAsync("webhook-qa", CancellationToken.None);

        keycloak.Requests.ShouldBe([Req("GET", "/clients?clientId=webhook-qa")]);
    }

    // ---- sub-groups -------------------------------------------------------------

    private static string Rows(int from, int count) =>
        "[" + string.Join(",", Enumerable.Range(from, count).Select(
            index => $$$"""{"id":"g-{{{index}}}","name":"fab-{{{index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)}}}","path":"/fabs/x"}""")) + "]";

    private static Func<StubKeycloakHandler, RecordedRequest, string?> GroupTree(
        Func<string, string> childrenFor) =>
        (_, request) =>
            request.PathAndQuery.Contains("/group-by-path/", StringComparison.Ordinal)
                ? """{"id":"99999999-9999-9999-9999-999999999999","name":"fabs","path":"/fabs"}"""
                : childrenFor(request.PathAndQuery);

    private const string ChildrenPath = "/groups/99999999-9999-9999-9999-999999999999/children";

    [Fact]
    public async Task Sub_groups_are_read_page_by_page_until_a_short_page()
    {
        StubKeycloakHandler keycloak = new(GroupTree(path =>
            path.EndsWith("first=0&max=100", StringComparison.Ordinal) ? Rows(0, 100) : Rows(100, 5)));

        var names = await ClientOver(keycloak).GetSubGroupNamesAsync("/fabs", CancellationToken.None);

        names.HasValue.ShouldBeTrue();
        names.Value.Count.ShouldBe(105);
        names.Value[0].ShouldBe("fab-000");
        names.Value[99].ShouldBe("fab-099");
        names.Value[100].ShouldBe("fab-100");
        names.Value[104].ShouldBe("fab-104");
        keycloak.Requests.ShouldBe(
            [
                Req("GET", "/group-by-path/fabs"),
                Req("GET", $"{ChildrenPath}?first=0&max=100"),
                Req("GET", $"{ChildrenPath}?first=100&max=100"),
            ]);
    }

    [Fact]
    public async Task A_full_page_is_followed_by_one_more_request_even_when_that_one_is_empty()
    {
        StubKeycloakHandler keycloak = new(GroupTree(path =>
            path.EndsWith("first=0&max=100", StringComparison.Ordinal) ? Rows(0, 100) : "[]"));

        var names = await ClientOver(keycloak).GetSubGroupNamesAsync("/fabs", CancellationToken.None);

        names.Value.Count.ShouldBe(100);
        keycloak.Requests.Select(request => request.PathAndQuery).ShouldBe(
            [
                $"{Base}/group-by-path/fabs",
                $"{Base}{ChildrenPath}?first=0&max=100",
                $"{Base}{ChildrenPath}?first=100&max=100",
            ]);
    }

    [Fact]
    public async Task Three_pages_advance_first_by_the_count_read_so_far()
    {
        StubKeycloakHandler keycloak = new(GroupTree(path => path switch
        {
            _ when path.EndsWith("first=0&max=100", StringComparison.Ordinal) => Rows(0, 100),
            _ when path.EndsWith("first=100&max=100", StringComparison.Ordinal) => Rows(100, 100),
            _ => Rows(200, 3),
        }));

        var names = await ClientOver(keycloak).GetSubGroupNamesAsync("/fabs", CancellationToken.None);

        names.Value.Count.ShouldBe(203);
        names.Value[^1].ShouldBe("fab-202");
        keycloak.Requests.Select(request => request.PathAndQuery).ShouldBe(
            [
                $"{Base}/group-by-path/fabs",
                $"{Base}{ChildrenPath}?first=0&max=100",
                $"{Base}{ChildrenPath}?first=100&max=100",
                $"{Base}{ChildrenPath}?first=200&max=100",
            ]);
    }

    [Fact]
    public async Task A_parent_that_lists_its_sub_groups_inline_is_answered_without_a_children_request()
    {
        StubKeycloakHandler keycloak = new((_, _) =>
            """{"id":"99999999-9999-9999-9999-999999999999","name":"fabs","path":"/fabs","subGroups":[{"id":"a","name":"munich","path":"/fabs/munich"},{"id":"b","name":"dresden","path":"/fabs/dresden"}]}""");

        var names = await ClientOver(keycloak).GetSubGroupNamesAsync("/fabs", CancellationToken.None);

        names.Value.ShouldBe(["munich", "dresden"]);
        keycloak.Requests.ShouldBe([Req("GET", "/group-by-path/fabs")]);
    }

    [Fact]
    public async Task A_parent_missing_from_the_realm_is_absent_after_one_request()
    {
        StubKeycloakHandler keycloak = new((_, _) => "{}")
        {
            Refuse = _ => HttpStatusCode.NotFound,
        };

        var names = await ClientOver(keycloak).GetSubGroupNamesAsync("/fabs", CancellationToken.None);

        names.HasValue.ShouldBeFalse();
        keycloak.Requests.ShouldBe([Req("GET", "/group-by-path/fabs")]);
    }

    [Fact]
    public async Task A_refused_children_page_throws_after_the_pages_before_it()
    {
        StubKeycloakHandler keycloak = new(GroupTree(path =>
            (path.Contains("first=0&", StringComparison.Ordinal) || path.Contains("first=100&", StringComparison.Ordinal)) ? Rows(0, 100) : "[]"))
        {
            Refuse = request => request.PathAndQuery.EndsWith("first=100&max=100", StringComparison.Ordinal)
                ? HttpStatusCode.ServiceUnavailable : null,
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => ClientOver(keycloak).GetSubGroupNamesAsync("/fabs", CancellationToken.None));

        thrown.Message.ShouldBe("Response status code does not indicate success: 503 (Service Unavailable).");
        keycloak.Requests.Count.ShouldBe(3);
    }

    // ---- sweep reads --------------------------------------------------------------

    [Fact]
    public async Task The_enrolled_kiosk_query_and_the_stamped_client_query_each_send_one_get_of_the_client_list()
    {
        StubKeycloakHandler keycloak = new((_, _) =>
            """[{"id":"a","clientId":"kiosk.munich.press-1","enabled":true,"attributes":{"sse.kind":"kiosk"}},{"id":"b","clientId":"plc.munich.oven-1","enabled":false,"attributes":{"sse.kind":"device"}},{"id":"c","clientId":"management-web","enabled":true}]""");
        var client = ClientOver(keycloak);

        var kiosks = await client.GetEnrolledKioskClientIdsAsync(CancellationToken.None);
        var stamped = await client.GetStampedClientsAsync(CancellationToken.None);

        kiosks.ShouldBe(["kiosk.munich.press-1"]);
        stamped.ShouldBe(
            [
                new StampedClient("kiosk.munich.press-1", "kiosk", Enabled: true),
                new StampedClient("plc.munich.oven-1", "device", Enabled: false),
            ]);
        keycloak.Requests.ShouldBe([Req("GET", "/clients"), Req("GET", "/clients")]);
    }

    [Fact]
    public async Task The_service_account_age_probes_the_client_then_reads_its_service_account_user()
    {
        StubKeycloakHandler keycloak = new((_, request) =>
            request.PathAndQuery.Contains("clientId=", StringComparison.Ordinal)
                ? $$$"""[{"id":"{{{NewUuid}}}","clientId":"plc.munich.oven-1","enabled":true}]"""
                : """{"id":"u-1","createdTimestamp":1760000000000}""");

        var createdAt = await ClientOver(keycloak)
            .GetServiceAccountCreatedAtAsync("plc.munich.oven-1", CancellationToken.None);

        createdAt.Value.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1760000000000));
        keycloak.Requests.ShouldBe(
            [
                Req("GET", "/clients?clientId=plc.munich.oven-1"),
                Req("GET", $"/clients/{NewUuid}/service-account-user"),
            ]);
    }

    [Fact]
    public async Task The_service_account_age_of_an_unknown_client_is_absent_after_the_probe()
    {
        StubKeycloakHandler keycloak = new((_, _) => "[]");

        var createdAt = await ClientOver(keycloak)
            .GetServiceAccountCreatedAtAsync("plc.munich.oven-1", CancellationToken.None);

        createdAt.HasValue.ShouldBeFalse();
        keycloak.Requests.ShouldBe([Req("GET", "/clients?clientId=plc.munich.oven-1")]);
    }

    [Fact]
    public async Task The_public_strip_probes_then_reads_the_user_then_the_roles_then_deletes_what_it_read()
    {
        StubKeycloakHandler keycloak = new((_, request) =>
        {
            var path = request.PathAndQuery;
            if (request.Method != "GET")
            {
                return null;
            }

            if (path.Contains("clientId=", StringComparison.Ordinal))
            {
                return $$$"""[{"id":"{{{NewUuid}}}","clientId":"{{{ClientId}}}","enabled":true}]""";
            }

            return path.EndsWith("/service-account-user", StringComparison.Ordinal)
                ? $$$"""{"id":"{{{UserUuid}}}"}"""
                : RolesJson;
        });

        var changed = await ClientOver(keycloak).StripInheritedRealmRolesAsync(ClientId, CancellationToken.None);

        changed.ShouldBeTrue();
        keycloak.Requests.ShouldBe(
            [
                Probe(),
                Req("GET", $"/clients/{NewUuid}/service-account-user"),
                Req("GET", $"/users/{UserUuid}/role-mappings/realm"),
                Req("DELETE", $"/users/{UserUuid}/role-mappings/realm", RolesJson),
            ]);
    }

    [Fact]
    public async Task The_public_strip_of_an_already_stripped_account_sends_no_delete()
    {
        StubKeycloakHandler keycloak = new((_, request) =>
        {
            var path = request.PathAndQuery;
            if (path.Contains("clientId=", StringComparison.Ordinal))
            {
                return $$$"""[{"id":"{{{NewUuid}}}","clientId":"{{{ClientId}}}","enabled":true}]""";
            }

            return path.EndsWith("/service-account-user", StringComparison.Ordinal)
                ? $$$"""{"id":"{{{UserUuid}}}"}"""
                : "[]";
        });

        var changed = await ClientOver(keycloak).StripInheritedRealmRolesAsync(ClientId, CancellationToken.None);

        changed.ShouldBeFalse();
        keycloak.Requests.Count.ShouldBe(3);
        keycloak.Requests.ShouldNotContain(request => request.Method == "DELETE");
    }

    [Fact]
    public async Task The_public_strip_of_an_unknown_client_stops_after_the_probe()
    {
        StubKeycloakHandler keycloak = new((_, _) => "[]");

        var thrown = await Should.ThrowAsync<KeycloakClientNotFoundException>(
            () => ClientOver(keycloak).StripInheritedRealmRolesAsync(ClientId, CancellationToken.None));

        thrown.Message.ShouldBe("Keycloak client 'kiosk.munich.press-1' not found.");
        keycloak.Requests.ShouldBe([Probe()]);
    }
}
