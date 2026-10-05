using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 004 T094 — drives the US4 branch/edit/publish flow through the
/// API: publish v1, branch a draft v2, edit v2's label, publish v2,
/// and assert v1 is atomically Archived while v2 is Published
/// (FR-003 atomic-swap on a multi-revision overlay chain).
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayRevisionLifecycleIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object SampleLabelBody(string text = "Production Line 1", int fontSizePx = 48) => new
    {
        kind = "Text",
        color = "#FFFFFFD9",
        text,
        normalizedX = 0.5m,
        normalizedY = 0.05m,
        normalizedWidth = 0.3m,
        normalizedHeight = 0.08m,
        fontSizePx,
    };

    [Fact]
    public async Task Publish_a_new_revision_atomically_archives_the_previous_published_revision()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Rev-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody() },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        // v1 → Published.
        HttpResponseMessage publishOne = await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/1/publish");
        publishOne.EnsureSuccessStatusCode();

        // Branch v2 (Draft, label inherited from v1).
        HttpResponseMessage branched = await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"draft");
        branched.StatusCode.ShouldBe(HttpStatusCode.Created);
        int v2 = await branched.Content.ReadFromJsonAsync<int>();
        v2.ShouldBe(2);

        // Edit v2's label.
        HttpResponseMessage edited = await OverlayRequests.PatchAsync(
            overlays, overlayIdentifier, $"revisions/{v2}",
            new { elements = new[] { SampleLabelBody("Updated label", 64) } });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Publish v2 → atomic swap: v1 becomes Archived, v2 becomes Published.
        HttpResponseMessage publishTwo = await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/{v2}/publish");
        publishTwo.EnsureSuccessStatusCode();

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement revisions = payload.GetProperty("revisions");
        revisions.GetArrayLength().ShouldBe(2);

        JsonElement r1 = revisions.EnumerateArray().Single(r => r.GetProperty("revisionNumber").GetInt32() == 1);
        JsonElement r2 = revisions.EnumerateArray().Single(r => r.GetProperty("revisionNumber").GetInt32() == 2);
        r1.GetProperty("state").GetString().ShouldBe("Archived");
        r2.GetProperty("state").GetString().ShouldBe("Published");
        JsonElement r2Label = r2.GetProperty("elements")[0];
        r2Label.GetProperty("text").GetString().ShouldBe("Updated label");
        r2Label.GetProperty("fontSizePx").GetInt32().ShouldBe(64);
    }

    [Fact]
    public async Task Revert_brings_a_Published_revision_back_to_Draft()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Rvt-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody() },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage publish = await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/1/publish");
        publish.EnsureSuccessStatusCode();

        HttpResponseMessage revert = await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/1/revert");
        revert.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement revision = payload.GetProperty("revisions")[0];
        revision.GetProperty("state").GetString().ShouldBe("Draft");
        revision.GetProperty("publishedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// Spec 037 T024 (ADR-0121) — recovery over real SQL, end to end. The twin
    /// of the LayoutComposition test, and required for the same reason.
    ///
    /// <para>
    /// The recovered draft clones the archived revision's <b>EF-owned</b> Label
    /// under a new owner in the same change-tracker. <c>Revision.Branch</c>'s own
    /// comment explains that sharing the CLR instance makes EF try to re-key the
    /// owned entity onto a new principal and throw — written for the
    /// published-source case. A hand-written fake models that away by
    /// construction and cannot answer whether it holds here.
    /// </para>
    ///
    /// <para>
    /// Asserts <b>branch, edit and publish</b>, not just the branch. A draft
    /// nobody can publish leaves the overlay exactly as unusable as before.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_archived_overlay_can_be_branched_edited_and_published_again()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Recov-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody("Rolling Mill A", 64) },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, "revisions/1/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, "revisions/1/archive"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Stranded before spec 037: no Published revision to branch from and no
        // Draft to publish.
        HttpResponseMessage branched = await OverlayRequests.PostAsync(overlays, overlayIdentifier, "draft");
        branched.StatusCode.ShouldBe(HttpStatusCode.Created);
        int recovered = await branched.Content.ReadFromJsonAsync<int>();
        recovered.ShouldBe(2);

        // FR-002: the label came back with it, geometry included.
        HttpResponseMessage afterBranch = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement branchedRevision = (await afterBranch.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")
            .EnumerateArray()
            .Single(revision => revision.GetProperty("revisionNumber").GetInt32() == recovered);
        branchedRevision.GetProperty("state").GetString().ShouldBe("Draft");
        JsonElement branchedLabel = branchedRevision.GetProperty("elements")[0];
        branchedLabel.GetProperty("text").GetString().ShouldBe("Rolling Mill A");
        branchedLabel.GetProperty("fontSizePx").GetInt32().ShouldBe(64);

        (await OverlayRequests.PatchAsync(
            overlays,
            overlayIdentifier,
            $"revisions/{recovered}",
            new { elements = new[] { SampleLabelBody("Rolling Mill B", 32) } })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/{recovered}/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // FR-003: same chain, same identifier, the archived revision still there.
        HttpResponseMessage finished = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement payload = await finished.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("overlayIdentifier").GetGuid().ShouldBe(overlayIdentifier);
        JsonElement revisions = payload.GetProperty("revisions");
        revisions.GetArrayLength().ShouldBe(2);
        revisions.EnumerateArray()
            .Single(revision => revision.GetProperty("revisionNumber").GetInt32() == 1)
            .GetProperty("state").GetString().ShouldBe("Archived");
        JsonElement live = revisions.EnumerateArray()
            .Single(revision => revision.GetProperty("revisionNumber").GetInt32() == recovered);
        live.GetProperty("state").GetString().ShouldBe("Published");
        live.GetProperty("elements")[0].GetProperty("text").GetString().ShouldBe("Rolling Mill B");
    }

    /// <summary>
    /// Spec 150 (#2345) FR-007 — the highest-risk line in the backend change,
    /// and why this must be an integration test rather than a unit test: a
    /// shallow copy shares the base revision's EF-owned <c>Label</c> (and its
    /// owned <c>Position</c>/<c>Size</c>) across two revisions, and EF throws
    /// trying to re-key an owned entity onto a new principal on
    /// <c>SaveChanges</c> — a failure a hand-written fake repository cannot
    /// reproduce.
    ///
    /// <para>
    /// Spec 300 (#2349, ADR-0165) widens the trap one level down: the owned
    /// reference that must not be shared across revisions is now the
    /// optional <c>Text</c> component too, and <c>Color</c> is a second
    /// per-element scalar that a careless copy could leave aliased or
    /// defaulted. A set of three same-kind Texts cannot exercise the
    /// nullable-component case at all, so this branches a mixed
    /// <c>[Box, Text, Ellipse]</c> set and asserts every kind's <c>Color</c>
    /// and nullable <c>Text</c> came through the deep copy distinctly.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Branching_a_mixed_kind_revision_deep_copies_every_element_and_saves()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Branch3-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new object[]
                {
                    new { kind = "Box", color = "#1565C0FF", normalizedX = 0.1m, normalizedY = 0.1m, normalizedWidth = 0.2m, normalizedHeight = 0.2m },
                    new { kind = "Text", color = "#FFFFFFD9", text = "Second", normalizedX = 0.2m, normalizedY = 0.2m, normalizedWidth = 0.2m, normalizedHeight = 0.2m, fontSizePx = 20 },
                    new { kind = "Ellipse", color = "#2E7D32FF", normalizedX = 0.3m, normalizedY = 0.3m, normalizedWidth = 0.2m, normalizedHeight = 0.2m },
                },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, "revisions/1/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The failure this guards against surfaces here, as a 500 from EF's
        // SaveChanges inside BranchDraftRevisionCommandHandler — not as a
        // domain exception the API translates to a 4xx.
        HttpResponseMessage branched = await OverlayRequests.PostAsync(overlays, overlayIdentifier, "draft");
        branched.StatusCode.ShouldBe(HttpStatusCode.Created, await branched.Content.ReadAsStringAsync());
        int draftNumber = await branched.Content.ReadFromJsonAsync<int>();

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement draft = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")
            .EnumerateArray()
            .Single(revision => revision.GetProperty("revisionNumber").GetInt32() == draftNumber);
        JsonElement elements = draft.GetProperty("elements");
        elements.GetArrayLength().ShouldBe(3);

        JsonElement box = elements[0];
        box.GetProperty("kind").GetString().ShouldBe("Box");
        box.GetProperty("color").GetString().ShouldBe("#1565C0FF");
        box.GetProperty("normalizedX").GetDecimal().ShouldBe(0.1m);
        box.GetProperty("normalizedY").GetDecimal().ShouldBe(0.1m);
        box.GetProperty("text").ValueKind.ShouldBe(JsonValueKind.Null);
        box.GetProperty("fontSizePx").ValueKind.ShouldBe(JsonValueKind.Null);

        JsonElement text = elements[1];
        text.GetProperty("kind").GetString().ShouldBe("Text");
        text.GetProperty("color").GetString().ShouldBe("#FFFFFFD9");
        text.GetProperty("normalizedX").GetDecimal().ShouldBe(0.2m);
        text.GetProperty("normalizedY").GetDecimal().ShouldBe(0.2m);
        text.GetProperty("text").GetString().ShouldBe("Second");
        text.GetProperty("fontSizePx").GetInt32().ShouldBe(20);

        JsonElement ellipse = elements[2];
        ellipse.GetProperty("kind").GetString().ShouldBe("Ellipse");
        ellipse.GetProperty("color").GetString().ShouldBe("#2E7D32FF");
        ellipse.GetProperty("normalizedX").GetDecimal().ShouldBe(0.3m);
        ellipse.GetProperty("normalizedY").GetDecimal().ShouldBe(0.3m);
        ellipse.GetProperty("text").ValueKind.ShouldBe(JsonValueKind.Null);
        ellipse.GetProperty("fontSizePx").ValueKind.ShouldBe(JsonValueKind.Null);

        // Publishing the branch is the second half of FR-007's proof: a
        // deep-copied-but-corrupted set could still read back correctly and
        // fail only on the next write.
        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/{draftNumber}/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Should-fix coverage (spec 150, #2345) for the public edit behaviour:
    /// replacing one label while resubmitting the other two unchanged must
    /// still come back in ordinal order.
    ///
    /// <para>
    /// This is edit-path regression coverage, <b>not</b> a reproduction of
    /// the ordering gotcha the fix guards against: run against the reverted
    /// fix (<c>Labels => labels;</c>, no sort), this test still passes —
    /// confirmed directly, not assumed. <c>ReplaceLabels</c> clears and
    /// re-adds the whole set on every edit, so EF deletes and re-inserts all
    /// three rows in ordinal order on every write; a tiny freshly-rewritten
    /// table's physical scan order already matches ordinal order under this
    /// app's query shape. It stays here because it is real behaviour coverage
    /// for the edit path (same-size, see the shrink/grow siblings below for
    /// the other two), checked against every label's full geometry, not just
    /// text — not because it proves the sort is necessary.
    /// <see cref="A_label_set_written_out_of_physical_order_still_reads_back_in_ordinal_order"/>
    /// below was the attempt at an actual counterfactual; see its own comment
    /// for what was actually observed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Replacing_one_label_returns_the_whole_set_in_ordinal_order()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Ord3-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody("A"), SampleLabelBody("B"), SampleLabelBody("C") },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        // Only ordinal 0's row changes content; ordinal 1 and 2 are
        // resubmitted unchanged. If the read path relied on physical row
        // order instead of sorting by ordinal, this is exactly the shape
        // that scrambles it.
        HttpResponseMessage edited = await OverlayRequests.PatchAsync(
            overlays, overlayIdentifier, "revisions/1",
            new { elements = new[] { SampleLabelBody("X"), SampleLabelBody("B"), SampleLabelBody("C") } });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement elements = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0]
            .GetProperty("elements");

        elements.GetArrayLength().ShouldBe(3);
        elements[0].GetProperty("text").GetString().ShouldBe("X");
        elements[1].GetProperty("text").GetString().ShouldBe("B");
        elements[2].GetProperty("text").GetString().ShouldBe("C");
    }

    /// <summary>
    /// Shrink case for the same blocker: 3 labels down to 1.
    /// </summary>
    [Fact]
    public async Task Shrinking_the_label_set_returns_the_remaining_label_in_ordinal_order()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Ord1-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody("A"), SampleLabelBody("B"), SampleLabelBody("C") },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage edited = await OverlayRequests.PatchAsync(
            overlays, overlayIdentifier, "revisions/1",
            new { elements = new[] { SampleLabelBody("OnlyOne") } });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement elements = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0]
            .GetProperty("elements");

        elements.GetArrayLength().ShouldBe(1);
        elements[0].GetProperty("text").GetString().ShouldBe("OnlyOne");
    }

    /// <summary>
    /// Grow case for the same blocker: 1 label up to 3.
    /// </summary>
    [Fact]
    public async Task Growing_the_label_set_returns_every_label_in_ordinal_order()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"OrdG-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody("Solo") },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage edited = await OverlayRequests.PatchAsync(
            overlays, overlayIdentifier, "revisions/1",
            new { elements = new[] { SampleLabelBody("One"), SampleLabelBody("Two"), SampleLabelBody("Three") } });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement elements = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0]
            .GetProperty("elements");

        elements.GetArrayLength().ShouldBe(3);
        elements[0].GetProperty("text").GetString().ShouldBe("One");
        elements[1].GetProperty("text").GetString().ShouldBe("Two");
        elements[2].GetProperty("text").GetString().ShouldBe("Three");
    }

    /// <summary>
    /// Phase-6 blocker (spec 150, #2345) — the attempt at an actual
    /// counterfactual for "<c>Revision.Labels</c> had no ordering guarantee".
    /// The app-level edit path (above) cannot manufacture the defect because
    /// <c>ReplaceLabels</c> always deletes and re-inserts the whole set in
    /// ordinal order, so a freshly-rewritten table's physical scan order
    /// already agrees with ordinal order under this app's query shape. This
    /// test bypasses the app entirely for ordinal 1 and 2: it inserts their
    /// rows directly via SQL in the <b>reverse</b> of ordinal order (ordinal
    /// 2's row physically before ordinal 1's) — a shape a Postgres heap can
    /// end up in after out-of-order writes the application never controls
    /// (HOT updates, autovacuum, concurrent sessions), though which exact
    /// mechanism isn't claimed here.
    ///
    /// <para>
    /// <b>Run, honestly, against the reverted fix</b> (<c>Labels => labels;</c>,
    /// no sort) rather than assumed: this test still passed — <c>GET
    /// /overlays/{id}</c> returned ordinal order [A, B, C] even with the rows
    /// physically reversed and no explicit sort in the domain. This Postgres
    /// query plan apparently orders owned-collection rows by key during
    /// materialization for the query shapes this app actually issues; EF
    /// Core does not document or guarantee this, so it is not something to
    /// rely on, but it means this test is not a disproof-by-counterexample —
    /// it is regression/documentation coverage for the contract
    /// <see cref="SmartSentinelEye.OverlayDesigner.Domain.Overlay.Revision.Elements"/>
    /// now owns explicitly, not evidence the bug was ever observed to fire.
    /// No fabricated transcript exists for this test; if a future change to
    /// the query shape (a split query, a raw-SQL read path, a planner
    /// change) does make it fail, that failure — not this comment — is the
    /// real evidence.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_label_set_written_out_of_physical_order_still_reads_back_in_ordinal_order()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"OrdRaw-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody("A") },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        // Physically out of ordinal order: ordinal 2's row lands in the heap
        // before ordinal 1's.
        await InsertRawLabelAsync(overlayIdentifier, ordinal: 2, text: "C");
        await InsertRawLabelAsync(overlayIdentifier, ordinal: 1, text: "B");

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement elements = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0]
            .GetProperty("elements");

        elements.GetArrayLength().ShouldBe(3);
        elements[0].GetProperty("text").GetString().ShouldBe("A");
        elements[1].GetProperty("text").GetString().ShouldBe("B");
        elements[2].GetProperty("text").GetString().ShouldBe("C");
    }

    /// <summary>
    /// Spec 301 (#2348) US3, T003 — the ordering premise this spec's whole
    /// PR-A rests on: a wholesale <c>PATCH</c> that only permutes an existing
    /// element set (spec 150 FR-006) is published and read back in the
    /// permuted order, end to end through the real API and the real
    /// published event. Spec 150/300 already made the ordinal both the
    /// stored key and the paint order, so this is <b>expected to pass on
    /// today's unmodified code</b> — if it does not, US1/US3's premise that
    /// the backend already orders reorders correctly is false, and that is
    /// a finding to report, not something to fix here (US3 touches no
    /// production code).
    /// </summary>
    [Fact]
    public async Task Reordering_through_a_branch_PATCH_republishes_elements_in_the_new_order()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Reord-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new object[]
                {
                    new { kind = "Box", color = "#1565C0FF", normalizedX = 0.1m, normalizedY = 0.1m, normalizedWidth = 0.2m, normalizedHeight = 0.2m },
                    new { kind = "Text", color = "#FFFFFFD9", text = "Zone A", normalizedX = 0.5m, normalizedY = 0.05m, normalizedWidth = 0.3m, normalizedHeight = 0.08m, fontSizePx = 48 },
                    new { kind = "Ellipse", color = "#2E7D32FF", normalizedX = 0.3m, normalizedY = 0.3m, normalizedWidth = 0.2m, normalizedHeight = 0.2m },
                },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, "revisions/1/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage branched = await OverlayRequests.PostAsync(overlays, overlayIdentifier, "draft");
        branched.StatusCode.ShouldBe(HttpStatusCode.Created, await branched.Content.ReadAsStringAsync());
        int draftNumber = await branched.Content.ReadFromJsonAsync<int>();

        // The SAME three elements as revision 1, reordered: [Ellipse, Text, Box].
        object[] reordered =
        [
            new { kind = "Ellipse", color = "#2E7D32FF", normalizedX = 0.3m, normalizedY = 0.3m, normalizedWidth = 0.2m, normalizedHeight = 0.2m },
            new { kind = "Text", color = "#FFFFFFD9", text = "Zone A", normalizedX = 0.5m, normalizedY = 0.05m, normalizedWidth = 0.3m, normalizedHeight = 0.08m, fontSizePx = 48 },
            new { kind = "Box", color = "#1565C0FF", normalizedX = 0.1m, normalizedY = 0.1m, normalizedWidth = 0.2m, normalizedHeight = 0.2m },
        ];

        HttpResponseMessage edited = await OverlayRequests.PatchAsync(
            overlays, overlayIdentifier, $"revisions/{draftNumber}", new { elements = reordered });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        HttpResponseMessage fetchedDraft = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement draftElements = (await fetchedDraft.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")
            .EnumerateArray()
            .Single(revision => revision.GetProperty("revisionNumber").GetInt32() == draftNumber)
            .GetProperty("elements");
        draftElements.GetArrayLength().ShouldBe(3);
        draftElements[0].GetProperty("kind").GetString().ShouldBe("Ellipse");
        draftElements[1].GetProperty("kind").GetString().ShouldBe("Text");
        draftElements[2].GetProperty("kind").GetString().ShouldBe("Box");

        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/{draftNumber}/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpClient auditReader = await aspire.CreateAdminClientAsync("audit-observability");
        JsonElement payload = await PollForPublishedElementsStartingWithAsync(auditReader, overlayIdentifier, firstElementKind: "Ellipse");

        JsonElement publishedElements = payload.GetProperty("Elements");
        publishedElements.GetArrayLength().ShouldBe(3);
        publishedElements[0].GetProperty("Kind").GetString().ShouldBe("Ellipse");
        publishedElements[1].GetProperty("Kind").GetString().ShouldBe("Text");
        publishedElements[2].GetProperty("Kind").GetString().ShouldBe("Box");
    }

    /// <summary>
    /// Spec 301 (#2348) US3, T003 — the concurrency half. A reorder is a
    /// wholesale <c>PATCH</c> replace (spec 150 FR-006), and
    /// <see cref="SmartSentinelEye.Integration.Tests.OverlayDesigner.OverlayETagIntegrationTests.A_mutation_carrying_a_superseded_version_is_refused_with_409"/>
    /// already proves a stale <c>If-Match</c> is refused with 409 for a
    /// lifecycle transition (publish/archive) — not for a <c>PATCH</c> that
    /// replaces the element set, and not for a reorder specifically. No
    /// existing test covers that exact shape (checked via
    /// <c>grep -rn "If-Match\|StaleVersion\|409" tests/Integration.Tests/OverlayDesigner/</c>),
    /// so this adds it rather than duplicating.
    /// </summary>
    [Fact]
    public async Task Two_reorders_under_the_same_stale_If_Match_the_second_is_refused_with_409_and_the_first_stands()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Stale-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody("A"), SampleLabelBody("B"), SampleLabelBody("C") },
            });
        created.EnsureSuccessStatusCode();
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        int readAt = await OverlayRequests.VersionAsync(overlays, overlayIdentifier);

        HttpRequestMessage firstReorderRequest = OverlayRequests.Conditional(HttpMethod.Patch, overlayIdentifier, "revisions/1", readAt);
        firstReorderRequest.Content = JsonContent.Create(new { elements = new[] { SampleLabelBody("C"), SampleLabelBody("B"), SampleLabelBody("A") } });
        HttpResponseMessage firstReorder = await overlays.SendAsync(firstReorderRequest);
        firstReorder.StatusCode.ShouldBe(HttpStatusCode.OK, await firstReorder.Content.ReadAsStringAsync());

        // Same (now superseded) version as the first reorder.
        HttpRequestMessage secondReorderRequest = OverlayRequests.Conditional(HttpMethod.Patch, overlayIdentifier, "revisions/1", readAt);
        secondReorderRequest.Content = JsonContent.Create(new { elements = new[] { SampleLabelBody("B"), SampleLabelBody("A"), SampleLabelBody("C") } });
        HttpResponseMessage secondReorder = await overlays.SendAsync(secondReorderRequest);

        secondReorder.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await secondReorder.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_REVISION_STALE");

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        JsonElement elements = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0]
            .GetProperty("elements");
        elements.GetArrayLength().ShouldBe(3);
        elements[0].GetProperty("text").GetString().ShouldBe("C");
        elements[1].GetProperty("text").GetString().ShouldBe("B");
        elements[2].GetProperty("text").GetString().ShouldBe("A");
    }

    /// <summary>
    /// Mirrors <c>OverlayLifecycleIntegrationTests.PollForOverlayPublishedPayloadAsync</c>
    /// (spec 300 T013), widened to pick out one specific published event by
    /// its first element's kind rather than assuming exactly one row exists —
    /// this test's overlay publishes twice (revision 1, then the reordered
    /// branch), so the "exactly one row" shape that helper's own comment
    /// relies on does not hold here and is not what this test is about.
    /// </summary>
    private static async Task<JsonElement> PollForPublishedElementsStartingWithAsync(
        HttpClient auditReader, Guid overlayIdentifier, string firstElementKind)
    {
        string query = $"/audit?eventKind=OverlayRevisionPublishedV3&resourceIdentifier={overlayIdentifier}&pageSize=10";

        for (int attempt = 0; attempt < 40; attempt++)
        {
            HttpResponseMessage response = await auditReader.GetAsync(query);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
            JsonElement rows = page.GetProperty("rows");
            foreach (JsonElement row in rows.EnumerateArray())
            {
                JsonElement candidate = JsonDocument.Parse(row.GetProperty("payload").GetString()!).RootElement;
                JsonElement candidateElements = candidate.GetProperty("Elements");
                if (candidateElements.GetArrayLength() > 0
                    && candidateElements[0].GetProperty("Kind").GetString() == firstElementKind)
                {
                    return candidate;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No OverlayRevisionPublishedV3 row for {overlayIdentifier} whose first element is {firstElementKind} appeared within 20s.");
    }

    /// <summary>
    /// Inserts one <c>overlay_revision_elements</c> row directly via SQL
    /// (spec 300, #2349, ADR-0165 — renamed from
    /// <c>overlay_revision_labels</c>) for the overlay's revision 1,
    /// bypassing the domain and EF entirely — mirrors
    /// <c>TileSpanIntegrationTests.InsertRawTileNamingNoSpanColumnAsync</c>.
    /// Geometry, kind and colour are fixed; only the ordinal and text vary,
    /// which is all the physical-order counterfactual needs.
    /// </summary>
    private async Task InsertRawLabelAsync(Guid overlayIdentifier, int ordinal, string text)
    {
        await using OverlayDesignerDbContext db = await aspire.CreateOverlayDesignerDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO overlay_revision_elements
                (revision_id, ordinal, kind, color, text, x, y, width, height, font_size_px)
            SELECT revision_id, {1}, 'Text', '#FFFFFFD9', {2}, 0.1, 0.1, 0.2, 0.2, 16
              FROM overlay_revisions
             WHERE overlay_id = {0} AND revision_number = 1;
            """,
            overlayIdentifier, ordinal, text);
    }
}
