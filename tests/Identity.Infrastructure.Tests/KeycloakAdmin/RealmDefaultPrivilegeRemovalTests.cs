using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 440 T-A (ADR-0181) — the two requests that take a privilege out of the
/// realm's default composite, pinned at the <see cref="HttpMessageHandler"/> seam
/// because the claim is about the wire: which role is read, which composite is
/// written, and that the body carries what the read returned.
/// </summary>
public class RealmDefaultPrivilegeRemovalTests
{
    private const string Realm = "smart-sentinel-eye";
    private const string RoleUuid = "11111111-2222-3333-4444-555555555555";
    private const string RoleJson = $$"""{"id":"{{RoleUuid}}","name":"offline_access","composite":false,"clientRole":false}""";
    private const string RolePath = $"/admin/realms/{Realm}/roles/offline_access";
    private const string CompositesPath = $"/admin/realms/{Realm}/roles/default-roles-{Realm}/composites";

    [Fact]
    public async Task Removing_a_privilege_reads_the_role_then_deletes_it_from_the_default_composite()
    {
        StubKeycloakHandler keycloak = new(RoleOrNothing);

        await ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);

        keycloak.Requests.Select(request => (request.Method, request.PathAndQuery)).ToArray().ShouldBe(
            [("GET", RolePath), ("DELETE", CompositesPath)]);
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
    public async Task A_second_removal_sends_the_same_two_requests_and_succeeds()
    {
        StubKeycloakHandler keycloak = new(RoleOrNothing);
        var client = ClientOver(keycloak);

        await client.RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);
        await client.RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None);

        keycloak.Requests.Select(request => (request.Method, request.PathAndQuery)).ToArray().ShouldBe(
            [
                ("GET", RolePath),
                ("DELETE", CompositesPath),
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
            Refuse = request => request.Method == "GET" ? refusal : null,
        };

        await Should.ThrowAsync<HttpRequestException>(
            () => ClientOver(keycloak).RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None));

        keycloak.Requests.ShouldNotContain(request => request.Method == "DELETE");
    }

    private static string? RoleOrNothing(StubKeycloakHandler _, RecordedRequest request) =>
        request.Method == "GET" ? RoleJson : null;

    private static HttpKeycloakAdminClient ClientOver(StubKeycloakHandler keycloak) =>
        new(new HttpClient(keycloak) { BaseAddress = new Uri("http://keycloak.test/") },
            Options.Create(new KeycloakAdminOptions { Realm = Realm }),
            NullLogger<HttpKeycloakAdminClient>.Instance);
}
