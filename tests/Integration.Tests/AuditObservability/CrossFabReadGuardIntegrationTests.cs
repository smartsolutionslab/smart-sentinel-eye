using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;

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
    [Fact]
    public async Task Munich_member_reads_its_own_fab_timeline_but_is_refused_another_fab()
    {
        // admin@munich.test is a member of /fabs/munich only.
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");
        Guid overlayId = Guid.CreateVersion7();

        HttpResponseMessage ownFab = await client.GetAsync($"/audit/overlay/{overlayId}?fabId=munich");
        ownFab.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage otherFab = await client.GetAsync($"/audit/overlay/{overlayId}?fabId=berlin");
        otherFab.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonElement problem = await otherFab.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    /// <summary>
    /// The invariant is "never a fab the caller does not hold" — not "always
    /// the caller's own fab". A cross-fab row carries no fab and so belongs to
    /// nobody's fab; excluding it hid camera, stream, layout, overlay and
    /// variable history from every operator, since all of those publish
    /// without one (#1300).
    ///
    /// <para>
    /// Seeds both a foreign row and a cross-fab row rather than asserting over
    /// whatever the run happens to have produced: the loop this replaced would
    /// have passed on an empty page.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Search_without_a_fab_filter_returns_the_callers_fabs_and_cross_fab_rows()
    {
        await SeedAsync(Row("berlin"), Row(fab: null));

        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");

        HttpResponseMessage response = await client.GetAsync("/audit?pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement[] rows = [.. page.GetProperty("rows").EnumerateArray()];

        string?[] fabs = [.. rows.Select(row => row.GetProperty("fab").GetString())];
        fabs.ShouldNotContain("berlin");
        fabs.ShouldContain((string?)null, "a cross-fab row must be readable by a fab-assigned caller");
    }

    private static AuditEvent Row(string? fab) =>
        AuditEvent.From(
            new V1Envelope(
                EventTypeName: "CameraRegisteredV1",
                OccurredAt: DateTimeOffset.UtcNow,
                Fab: fab is null
                    ? Option<FabIdentifier>.None
                    : Option<FabIdentifier>.Some(FabIdentifier.From(fab)),
                Actor: ActorIdentifier.System,
                ActorUsername: Option<string>.None,
                EventIdentifier: EventIdentifier.From(Guid.CreateVersion7()),
                Payload: """{"seeded":"cross-fab-read-guard"}"""),
            V1Mapping.Unmapped,
            new SystemClock());

    private async Task SeedAsync(params AuditEvent[] events)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        context.AuditEvents.AddRange(events);
        await context.SaveChangesAsync();
    }
}
