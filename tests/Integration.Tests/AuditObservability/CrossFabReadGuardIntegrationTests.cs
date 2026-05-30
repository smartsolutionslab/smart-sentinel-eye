using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 009 US2 (T056): the per-resource timeline endpoint runs the shared
/// fab guard (spec 008 FR-019). A single-fab operator can read its own fab's
/// timeline but is refused another fab's with <c>403
/// RESOURCE_FAB_NOT_AUTHORIZED</c>; the cross-cutting search (no fabId) is
/// scoped to the caller's fab membership.
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabReadGuardIntegrationTests(AspireFixture aspire)
{
    private readonly AspireFixture _aspire = aspire;

    [Fact]
    public async Task Munich_member_reads_its_own_fab_timeline_but_is_refused_another_fab()
    {
        // admin@munich.test is a member of /fabs/munich only.
        using HttpClient client = await _aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");
        Guid overlayId = Guid.CreateVersion7();

        HttpResponseMessage ownFab = await client.GetAsync($"/audit/overlay/{overlayId}?fabId=munich");
        ownFab.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage otherFab = await client.GetAsync($"/audit/overlay/{overlayId}?fabId=berlin");
        otherFab.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonElement problem = await otherFab.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    [Fact]
    public async Task Search_without_a_fab_filter_is_scoped_to_the_callers_fab_membership()
    {
        using HttpClient client = await _aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");

        HttpResponseMessage response = await client.GetAsync("/audit?pageSize=50");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (JsonElement row in page.GetProperty("rows").EnumerateArray())
        {
            // The handler restricts a fab-scoped caller to its own fab's rows;
            // cross-fab (fab = null) rows are excluded for a fab member.
            row.GetProperty("fab").GetString().ShouldBe("munich");
        }
    }
}
