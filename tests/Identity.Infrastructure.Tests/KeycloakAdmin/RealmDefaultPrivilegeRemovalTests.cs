using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 440 T-A (ADR-0181, revised by ADR-0186 note 5) — the three requests that take a
/// privilege out of the realm's default composite, pinned at the
/// <see cref="HttpMessageHandler"/> seam because the claim is about the wire: which
/// composite the realm says is its default, which role is read, which composite is
/// written, and that the body carries what the read returned.
/// </summary>
public class RealmDefaultPrivilegeRemovalTests
{
    private const string Realm = "smart-sentinel-eye";
    private const string RoleUuid = "11111111-2222-3333-4444-555555555555";
    private const string RoleJson = $$"""{"id":"{{RoleUuid}}","name":"offline_access","composite":false,"clientRole":false}""";
    private const string RealmPath = $"/admin/realms/{Realm}";
    private const string RolePath = $"/admin/realms/{Realm}/roles/offline_access";
    private const string CompositesPath = $"/admin/realms/{Realm}/roles/default-roles-{Realm}/composites";
    private const string RealmJson = $$$"""{"realm":"{{{Realm}}}","defaultRole":{"name":"default-roles-{{{Realm}}}"}}""";

    [Fact]
    public async Task Removing_a_privilege_reads_the_realm_then_the_role_then_deletes_it_from_the_default_composite()
    {
        StubKeycloakHandler keycloak = new(RoleOrNothing);

        await ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);

        keycloak.Requests.Select(request => (request.Method, request.PathAndQuery)).ToArray().ShouldBe(
            [("GET", RealmPath), ("GET", RolePath), ("DELETE", CompositesPath)]);
    }

    /// <summary>
    /// ADR-0186 note 5: the default role's name is what the realm reports, not a name
    /// derived from the realm's. Red until the adapter reads <c>defaultRole.name</c>.
    /// </summary>
    [Fact]
    public async Task The_delete_targets_the_composite_of_the_default_role_the_realm_reports()
    {
        StubKeycloakHandler keycloak = new(
            (_, request) => request.PathAndQuery == RealmPath
                ? $$$"""{"realm":"{{{Realm}}}","defaultRole":{"name":"custom-default"}}"""
                : RoleOrNothing(null!, request));

        await ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);

        keycloak.Requests.Single(request => request.Method == "DELETE").PathAndQuery.ShouldBe(
            $"/admin/realms/{Realm}/roles/custom-default/composites");
    }

    [Fact]
    public async Task A_default_role_name_with_reserved_characters_is_escaped_in_the_path()
    {
        StubKeycloakHandler keycloak = new(
            (_, request) => request.PathAndQuery == RealmPath
                ? $$$"""{"realm":"{{{Realm}}}","defaultRole":{"name":"default roles/x"}}"""
                : RoleOrNothing(null!, request));

        await ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);

        keycloak.Requests.Single(request => request.Method == "DELETE").PathAndQuery.ShouldBe(
            $"/admin/realms/{Realm}/roles/default%20roles%2Fx/composites");
    }

    [Theory]
    [InlineData("""{"realm":"smart-sentinel-eye"}""")]
    [InlineData("""{"realm":"smart-sentinel-eye","defaultRole":{}}""")]
    [InlineData("""{"realm":"smart-sentinel-eye","defaultRole":{"name":""}}""")]
    public async Task A_realm_that_reports_no_default_role_name_throws_naming_the_realm_and_sends_no_delete(
        string realmRepresentation)
    {
        StubKeycloakHandler keycloak = new(
            (_, request) => request.PathAndQuery == RealmPath ? realmRepresentation : RoleOrNothing(null!, request));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None));

        thrown.Message.ShouldContain(Realm);
        keycloak.Requests.ShouldNotContain(request => request.Method == "DELETE");
    }

    /// <summary>
    /// Spec 440 (phase-6 finding): a whitespace name is not a name. Red while the adapter
    /// tests only for the empty string and so DELETEs on <c>roles/%20/composites</c>.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    public async Task A_realm_that_reports_a_whitespace_default_role_name_throws_naming_the_realm_and_sends_no_delete(
        string name)
    {
        StubKeycloakHandler keycloak = new(
            (_, request) => request.PathAndQuery == RealmPath
                ? $$$"""{"realm":"{{{Realm}}}","defaultRole":{"name":"{{{name}}}"}}"""
                : RoleOrNothing(null!, request));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None));

        thrown.Message.ShouldContain(Realm);
        keycloak.Requests.ShouldNotContain(request => request.Method == "DELETE");
    }

    [Fact]
    public async Task The_delete_body_is_a_one_element_array_carrying_the_role_that_was_read()
    {
        StubKeycloakHandler keycloak = new(RoleOrNothing);

        await ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);

        using var body = JsonDocument.Parse(keycloak.Only("DELETE", "/composites").Body);
        body.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        body.RootElement.GetArrayLength().ShouldBe(1);
        body.RootElement[0].GetProperty("id").GetString().ShouldBe(RoleUuid);
        body.RootElement[0].GetProperty("name").GetString().ShouldBe("offline_access");
    }

    /// <summary>
    /// The provider answers 204 for a member already absent, so the adapter has no
    /// pre-read and no early return: the boot step runs every boot, and a guard
    /// would be a branch no input reaches.
    /// </summary>
    [Fact]
    public async Task A_second_removal_sends_the_same_three_requests_and_succeeds()
    {
        StubKeycloakHandler keycloak = new(RoleOrNothing);
        var client = ClientOver(keycloak);

        await client.RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);
        await client.RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);

        keycloak.Requests.Select(request => (request.Method, request.PathAndQuery)).ToArray().ShouldBe(
            [
                ("GET", RealmPath),
                ("GET", RolePath),
                ("DELETE", CompositesPath),
                ("GET", RealmPath),
                ("GET", RolePath),
                ("DELETE", CompositesPath),
            ]);
    }

    [Fact]
    public async Task A_refused_delete_throws()
    {
        StubKeycloakHandler keycloak = new(RoleOrNothing)
        {
            Refuse = request => request.Method == "DELETE" ? HttpStatusCode.Forbidden : null,
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None));

        thrown.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_role_that_cannot_be_read_throws_and_no_delete_is_sent(HttpStatusCode refusal)
    {
        StubKeycloakHandler keycloak = new(RoleOrNothing)
        {
            Refuse = request => request.Method == "GET" && request.PathAndQuery == RolePath ? refusal : null,
        };

        await Should.ThrowAsync<HttpRequestException>(
            () => ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None));

        keycloak.Requests.ShouldNotContain(request => request.Method == "DELETE");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_realm_that_cannot_be_read_throws_and_nothing_else_is_sent(HttpStatusCode refusal)
    {
        StubKeycloakHandler keycloak = new(RoleOrNothing)
        {
            Refuse = request => request.PathAndQuery == RealmPath ? refusal : null,
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None));

        thrown.StatusCode.ShouldBe(refusal);
        keycloak.Requests.Select(request => request.Method).ToArray().ShouldBe(["GET"]);
    }

    private static string? RoleOrNothing(StubKeycloakHandler _, RecordedRequest request)
    {
        if (request.Method != "GET")
        {
            return null;
        }

        return request.PathAndQuery == RealmPath ? RealmJson : RoleJson;
    }

    private static HttpKeycloakAdminClient ClientOver(StubKeycloakHandler keycloak) =>
        new(new HttpClient(keycloak) { BaseAddress = new Uri("http://keycloak.test/") },
            Options.Create(new KeycloakAdminOptions { Realm = Realm }),
            NullLogger<HttpKeycloakAdminClient>.Instance);
}
