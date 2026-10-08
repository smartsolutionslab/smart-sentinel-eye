using System.Net.Http.Headers;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Integration.Tests.Identity;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventSourceModeApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Phase 4a (spec 269 T003f) — spec.md FR-013. An event source must not be
/// able to relax its own policing, the same defect spec 143 FR-010 closed for
/// the registry, with the sign reversed: letting a source flip itself to
/// discovery is just as much a source deciding which event types are
/// legitimate as letting it register one.
///
/// <para>
/// Red on arrival — the <c>/event-sources</c> group is unmapped, so both
/// probes get 404, not 403. A planted event-source client rather than a
/// <c>management-web</c> token, for the reason spec 143 FR-010's testing note
/// gives: <c>management-web</c> carries every <c>sse.*</c> default client
/// scope regardless of what is requested, so it structurally cannot
/// demonstrate the negative.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventSourceModeAuthorizationIntegrationTests(AspireFixture aspire)
{
    private readonly RealmProbe realm = new(aspire);

    private static readonly string[] EventSourceScopes =
        ["sse-identity", "sse-groups", "sse-audience", "sse.events.write"];

    [Fact]
    public async Task An_event_source_token_can_neither_declare_nor_change_a_source_mode()
    {
        string clientId = $"event-source-mode-probe-{Guid.CreateVersion7():N}";
        await PlantEventSourceClientAsync(clientId);

        try
        {
            using HttpClient source = await EventSourceClientAsync(clientId);

            HttpResponseMessage declared = await DeclareAsync(source, "plc", "strict");
            declared.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                await Diagnose(declared) + Environment.NewLine
                + "spec.md FR-013 is the whole reason sse.events.types.write is reused here. A 201 "
                + "here means the POST is still declaring sse.events.write.");

            HttpResponseMessage changed = await ChangeAsync(source, "plc", "discovery", expectedVersion: null);
            changed.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                await Diagnose(changed) + Environment.NewLine
                + "403 before 428: the scope is enforced by the authorization middleware, not by a "
                + "branch inside the handler that a missing header returns ahead of");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }
    }

    /// <summary>
    /// T018 (spec 317, #2325) — FR-013. The same defect FR-013's own
    /// reasoning names, with the sign reversed again: an event source must
    /// not be able to walk its own policing back to undeclared any more than
    /// it can declare or change it.
    /// </summary>
    [Fact]
    public async Task An_event_source_token_cannot_undeclare_a_source_mode()
    {
        string clientId = $"event-source-undeclare-probe-{Guid.CreateVersion7():N}";
        await PlantEventSourceClientAsync(clientId);

        try
        {
            using HttpClient source = await EventSourceClientAsync(clientId);

            HttpResponseMessage refused = await DeleteAsync(source, "plc", expectedVersion: null);

            refused.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                await Diagnose(refused) + Environment.NewLine
                + "403 before 428: the scope is enforced by the authorization middleware, not by a "
                + "branch inside the handler that a missing header returns ahead of");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }
    }

    private async Task PlantEventSourceClientAsync(string clientId)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/clients",
            new
            {
                clientId,
                enabled = true,
                publicClient = true,
                standardFlowEnabled = false,
                serviceAccountsEnabled = false,
                directAccessGrantsEnabled = true,
                defaultClientScopes = EventSourceScopes,
                optionalClientScopes = Array.Empty<string>(),
            },
            CancellationToken.None);

        created.IsSuccessStatusCode.ShouldBeTrue(
            $"planting '{clientId}' answered {(int)created.StatusCode}: "
            + await created.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> EventSourceClientAsync(string clientId)
    {
        string jwt = await aspire.GetAccessTokenForClientAsync(clientId, DresdenOperator, OperatorPassword, "openid");

        HttpClient client = aspire.CreateServiceClient(ResourceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        return client;
    }

    private async Task<string> Diagnose(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs(ResourceName)}";
}
