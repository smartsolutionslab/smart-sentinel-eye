using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
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
    /// Spec 209 (#2506) US1: the per-resource timeline is the second of two
    /// read paths that unconditionally equality-filtered on <c>fab</c>,
    /// excluding every row that legitimately carries none — overlay lifecycle
    /// events chief among them (ADR-0115). <see cref="SearchAuditQueryHandler"/>
    /// already includes fab-neutral rows for a fab-assigned caller (#1300);
    /// this proves the timeline endpoint does too.
    ///
    /// <para>
    /// Seeds a fab-neutral row **and** a <c>berlin</c> row on the same overlay
    /// identifier so a deleted (rather than widened) predicate is caught too:
    /// presence of the fab-neutral row alone would still pass if the fix
    /// stopped filtering on fab at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_fab_neutral_row_is_returned_by_a_fab_scoped_resource_timeline()
    {
        Guid overlayIdentifier = Guid.CreateVersion7();
        await SeedAsync(
            OverlayRow(overlayIdentifier, fab: null),
            OverlayRow(overlayIdentifier, fab: "berlin"));

        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");

        HttpResponseMessage response = await client.GetAsync($"/audit/overlay/{overlayIdentifier}?fabId=munich");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement[] rows = [.. page.GetProperty("rows").EnumerateArray()];

        string?[] fabs = [.. rows.Select(row => row.GetProperty("fab").GetString())];
        fabs.ShouldContain((string?)null, "the fab-neutral overlay row must be reachable from its own fab-scoped timeline");
        fabs.ShouldNotContain("berlin", "a row belonging to another fab must stay excluded");
    }

    /// <summary>
    /// The invariant is "never a fab the caller does not hold" — not "always
    /// the caller's own fab". A cross-fab row carries no fab and so belongs to
    /// nobody's fab; excluding it hid that whole class of history from every
    /// operator, since every operator belongs to a fab (#1300).
    ///
    /// <para>
    /// The class is smaller than the enumeration this comment used to carry.
    /// Camera events already stamped the fab before this; variable events do
    /// now (#2068), and layout events (#2071). What legitimately publishes
    /// without one is overlay events, whose domain events carry no fab at all
    /// (ADR-0115), and retention, which spans fabs. A stream-health event's
    /// fab is nullable and may still arrive null (#2076).
    /// </para>
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

    /// <summary>
    /// Spec 082 US1 (#2068). The filed symptom, on the read surface: a variable
    /// archived in munich must not be visible to an operator whose only
    /// membership is berlin.
    ///
    /// <para>
    /// Drives the real publish path rather than seeding a row, because that is
    /// where the defect lives. A seeded row carries whatever fab the test wrote;
    /// only an archive performed through the API shows what
    /// <c>VariableArchivedDomainEventHandler</c> actually stamps. Both producer
    /// tests are silent about this: they assert the event, not who can read the
    /// row it becomes.
    /// </para>
    ///
    /// <para>
    /// Asserts the #1300 behaviour in the same breath, so the fix is shown not
    /// to have bought fab scoping by re-excluding rows that genuinely have no
    /// fab.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_variable_archived_in_munich_is_not_returned_to_a_berlin_only_operator()
    {
        string name = $"fab{Guid.NewGuid():N}"[..16];
        await ArchiveInMunichAsync(name);
        await SeedAsync(Row(fab: null));

        using HttpClient munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");
        JsonElement archived = await PollForArchiveRowAsync(munich, name);

        using HttpClient berlin = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "op-berlin@berlin.test", "Operator1234");
        JsonElement[] visible = await ArchiveRowsAsync(berlin);

        string[] leaked = [.. visible
            .Select(row => row.GetProperty("payload").GetString()!)
            .Where(payload => payload.Contains(name, StringComparison.Ordinal))];
        leaked.ShouldBeEmpty(
            $"a variable archived in munich must not reach a berlin-only operator (archived '{name}')");

        // The publisher is the only place that knows, so say what it recorded.
        archived.GetProperty("fab").GetString().ShouldBe("munich");

        // #1300, unchanged: a row that genuinely has no fab still reaches
        // everyone. Scoping must not be bought by excluding it again.
        HttpResponseMessage unfiltered = await berlin.GetAsync("/audit?pageSize=200");
        unfiltered.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement page = await unfiltered.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("fab").GetString())
            .ShouldContain((string?)null, "a cross-fab row must stay readable by a fab-assigned caller");
    }

    /// <summary>
    /// Spec 215 (#2507) US1, SC-1. <c>?fabId=</c> binds to
    /// <see cref="string.Empty"/> and today reaches
    /// <see cref="ServiceDefaults.Authorization.IFabAuthorizationGuard.EnsureAccessAsync"/>
    /// unparsed, whose own <c>Ensure.That</c> precondition throws an uncaught
    /// <c>ArgumentException</c> — a 500 for a caller mistake. RED until T005
    /// reorders the parse ahead of the guard.
    /// </summary>
    [Fact]
    public async Task An_empty_fab_on_a_resource_timeline_is_a_client_error()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");
        Guid overlayIdentifier = Guid.CreateVersion7();

        HttpResponseMessage response = await client.GetAsync($"/audit/overlay/{overlayIdentifier}?fabId=");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("AUDIT_INVALID_INPUT");
    }

    /// <summary>
    /// Spec 215 (#2507) US1, SC-2. Without this, a fix keyed on
    /// <see cref="string.Empty"/> alone would pass the empty case and leave
    /// whitespace 500ing — <c>IsNotNullOrWhiteSpace</c> is what actually
    /// throws, not an equality check against the empty string. RED until T005.
    /// </summary>
    [Fact]
    public async Task A_whitespace_fab_on_a_resource_timeline_is_a_client_error()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");
        Guid overlayIdentifier = Guid.CreateVersion7();

        HttpResponseMessage response = await client.GetAsync($"/audit/overlay/{overlayIdentifier}?fabId=%20");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("AUDIT_INVALID_INPUT");
    }

    /// <summary>
    /// Spec 215 (#2507) US2, SC-8. On <c>Search</c>, <c>fabId</c> is optional
    /// (<c>string?</c>) and <c>?fabId=</c> still binds to
    /// <see cref="string.Empty"/>, which is "is not null" today — the same
    /// uncaught throw as the timeline endpoint, eight lines above it (spec's
    /// Claim 4). After T006 widens the predicate, an empty fab must behave
    /// like an omitted one: scoped to the caller's own fabs, not a filter that
    /// matches nothing. Seeds a foreign row so a predicate that was deleted
    /// rather than widened would also be caught. RED until T006.
    /// </summary>
    [Fact]
    public async Task An_empty_fab_on_the_audit_search_spans_the_callers_fabs()
    {
        await SeedAsync(Row("munich"), Row("berlin"));

        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");

        HttpResponseMessage response = await client.GetAsync("/audit?fabId=&pageSize=200");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement[] rows = [.. page.GetProperty("rows").EnumerateArray()];

        string?[] fabs = [.. rows.Select(row => row.GetProperty("fab").GetString())];
        fabs.ShouldContain("munich", "an empty fabId must be treated as unset, not as a filter matching nothing");
        fabs.ShouldNotContain("berlin", "an empty fabId must still scope the search to the caller's own fab membership");
    }

    /// <summary>
    /// Spec 215 (#2507), SC-5. Pins the ordering decision: the fab guard must
    /// still win over a malformed resource identifier, so a caller refused a
    /// fab cannot learn whether their resource identifier was well-formed.
    /// Already true today — the guard is the handler's first statement — and
    /// must stay true after T005 moves the fab *parse* ahead of the guard
    /// without moving the resource parse ahead of it too. Characterisation:
    /// green before and after, unmodified.
    /// </summary>
    [Fact]
    public async Task A_cross_fab_timeline_is_refused_before_a_malformed_resource_is_parsed()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");
        string malformedResourceIdentifier = new('a', ResourceIdentifier.MaximumLength + 1);

        HttpResponseMessage response = await client.GetAsync(
            $"/audit/overlay/{malformedResourceIdentifier}?fabId=berlin");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    /// <summary>
    /// Spec 215 (#2507), SC-6. RED, not characterisation — re-derived from
    /// source rather than trusted from the original plan. The guard runs on
    /// the raw, unparsed <c>fabId</c> before
    /// <see cref="FabIdentifier.From"/>'s parse is ever reached, and does a
    /// plain groups-membership check with no grammar opinion — today this
    /// answers 403 <c>RESOURCE_FAB_NOT_AUTHORIZED</c>, refused by the
    /// authorization boundary rather than the input-validation one. T005's
    /// same parse-before-guard reorder that fixes the empty-fab case also
    /// makes a malformed fab reach the parse and answer 400
    /// <c>AUDIT_INVALID_INPUT</c> — a second, previously-unnoticed instance of
    /// the same defect, closed by the same fix.
    /// </summary>
    [Fact]
    public async Task A_malformed_fab_grammar_now_gets_a_client_error_not_an_authorization_refusal()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");
        Guid overlayIdentifier = Guid.CreateVersion7();

        HttpResponseMessage response = await client.GetAsync($"/audit/overlay/{overlayIdentifier}?fabId=NOT_A_FAB");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("AUDIT_INVALID_INPUT");
    }

    /// <summary>
    /// Spec 245 (#2530) US1, SC-1. <c>Search</c> runs the fab guard on the
    /// raw, unparsed <c>fabId</c> — <c>DefaultFabAuthorizationGuard</c> does
    /// plain string equality against the <c>groups</c> claim and has no
    /// grammar opinion, so a malformed value is refused by the authorization
    /// boundary (403) rather than the input-validation one (400). Its sibling
    /// endpoint, the per-resource timeline, already answers this exact input
    /// with 400 <c>AUDIT_INVALID_INPUT</c> (the malformed-fab-grammar test
    /// above, spec 215 / #2507); this proves <c>Search</c> now agrees. RED
    /// until the endpoint parses <c>fabId</c> before the guard.
    /// </summary>
    [Fact]
    public async Task A_malformed_fab_on_the_audit_search_is_a_client_error_not_an_authorization_refusal()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");

        HttpResponseMessage response = await client.GetAsync("/audit?fabId=NOT_A_FAB");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("AUDIT_INVALID_INPUT");
    }

    /// <summary>
    /// Spec 215 (#2507), SC-7. Characterisation: records that the
    /// required-parameter refusal for an entirely omitted <c>fabId</c> is
    /// ASP.NET's own, and is not being replaced. No <c>title</c> is asserted
    /// — the spec's own SC-7 pins only the status, because the framework's
    /// wording for a missing required parameter is not part of this
    /// endpoint's contract. Green before and after, unmodified.
    /// </summary>
    [Fact]
    public async Task An_omitted_fab_keeps_the_frameworks_own_refusal()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");
        Guid overlayIdentifier = Guid.CreateVersion7();

        HttpResponseMessage response = await client.GetAsync($"/audit/overlay/{overlayIdentifier}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Spec 215 (#2507) US2, SC-9. The case that catches T006 mis-written as
    /// "skip the guard" instead of "widen the predicate": a named fab the
    /// caller does not hold must still be refused after the predicate change.
    /// Green before and after, unmodified.
    /// </summary>
    [Fact]
    public async Task A_cross_fab_audit_search_is_still_refused()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "admin@munich.test", "Admin1234");

        HttpResponseMessage response = await client.GetAsync("/audit?fabId=berlin");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    /// <summary>
    /// Spec 215 (#2507), SC-10. The <c>sse.audit.read</c> policy runs before
    /// the handler; an empty fab must not become a way to reach handler code
    /// unauthenticated. Green before and after, unmodified.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_empty_fab_request_is_challenged()
    {
        using HttpClient client = aspire.CreateServiceClient("audit-observability");
        Guid overlayIdentifier = Guid.CreateVersion7();

        HttpResponseMessage response = await client.GetAsync($"/audit/overlay/{overlayIdentifier}?fabId=");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task ArchiveInMunichAsync(string name)
    {
        using HttpClient variables = await aspire.CreateAuthenticatedClientAsync(
            "system-variables", "admin@munich.test", "Admin1234");

        HttpResponseMessage defined = await variables.PostAsJsonAsync(
            "/system-variables",
            new { name, type = "Number", initialValue = "1" });
        defined.StatusCode.ShouldBe(HttpStatusCode.Created, await defined.Content.ReadAsStringAsync());

        HttpResponseMessage archived = await VariableRequests.ArchiveAsync(variables, name);
        archived.IsSuccessStatusCode.ShouldBeTrue(await archived.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement[]> ArchiveRowsAsync(HttpClient audit)
    {
        HttpResponseMessage response = await audit.GetAsync(
            "/audit?eventKind=SystemVariableArchivedV1&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. page.GetProperty("rows").EnumerateArray()];
    }

    /// <summary>
    /// The archived row as its own fab's operator sees it. Its absence
    /// elsewhere means nothing until it is known to exist.
    /// </summary>
    private static async Task<JsonElement> PollForArchiveRowAsync(HttpClient audit, string name)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            JsonElement[] rows = await ArchiveRowsAsync(audit);
            foreach (JsonElement row in rows)
            {
                if (row.GetProperty("payload").GetString()!.Contains(name, StringComparison.Ordinal))
                {
                    return row;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No SystemVariableArchivedV1 audit row for '{name}' appeared within 20s.");
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

    /// <summary>
    /// Unlike <see cref="Row"/>, this pivots on resource kind "overlay" +
    /// <paramref name="overlayIdentifier"/> so it is reachable through the
    /// per-resource timeline endpoint, not just the cross-cutting search.
    /// </summary>
    private static AuditEvent OverlayRow(Guid overlayIdentifier, string? fab) =>
        AuditEvent.From(
            new V1Envelope(
                EventTypeName: "OverlayRevisionPublishedV1",
                OccurredAt: DateTimeOffset.UtcNow,
                Fab: fab is null
                    ? Option<FabIdentifier>.None
                    : Option<FabIdentifier>.Some(FabIdentifier.From(fab)),
                Actor: ActorIdentifier.System,
                ActorUsername: Option<string>.None,
                EventIdentifier: EventIdentifier.From(Guid.CreateVersion7()),
                Payload: """{"seeded":"cross-fab-read-guard"}"""),
            new V1Mapping(
                Option<ResourceKind>.Some(ResourceKind.Overlay),
                Option<ResourceIdentifier>.Some(ResourceIdentifier.From(overlayIdentifier.ToString()))),
            new SystemClock());

    private async Task SeedAsync(params AuditEvent[] events)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        context.AuditEvents.AddRange(events);
        await context.SaveChangesAsync();
    }
}
