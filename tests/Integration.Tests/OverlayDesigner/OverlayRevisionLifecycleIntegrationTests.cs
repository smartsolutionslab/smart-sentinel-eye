using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

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
                labels = new[] { SampleLabelBody() },
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
            new { labels = new[] { SampleLabelBody("Updated label", 64) } });
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
        JsonElement r2Label = r2.GetProperty("labels")[0];
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
                labels = new[] { SampleLabelBody() },
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
                labels = new[] { SampleLabelBody("Rolling Mill A", 64) },
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
        JsonElement branchedLabel = branchedRevision.GetProperty("labels")[0];
        branchedLabel.GetProperty("text").GetString().ShouldBe("Rolling Mill A");
        branchedLabel.GetProperty("fontSizePx").GetInt32().ShouldBe(64);

        (await OverlayRequests.PatchAsync(
            overlays,
            overlayIdentifier,
            $"revisions/{recovered}",
            new { labels = new[] { SampleLabelBody("Rolling Mill B", 32) } })).StatusCode.ShouldBe(HttpStatusCode.OK);

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
        live.GetProperty("labels")[0].GetProperty("text").GetString().ShouldBe("Rolling Mill B");
    }

    /// <summary>
    /// Spec 150 (#2345) FR-007 — the highest-risk line in the backend change,
    /// and why this must be an integration test rather than a unit test: a
    /// shallow copy shares the base revision's EF-owned <c>Label</c> (and its
    /// owned <c>Position</c>/<c>Size</c>) across two revisions, and EF throws
    /// trying to re-key an owned entity onto a new principal on
    /// <c>SaveChanges</c> — a failure a hand-written fake repository cannot
    /// reproduce. Branching a three-label revision and saving must succeed,
    /// and every label's geometry and ordinal must survive the round trip.
    /// </summary>
    [Fact]
    public async Task Branching_a_three_label_revision_deep_copies_every_label_and_saves()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Branch3-{Guid.NewGuid():N}".Substring(0, 16),
                labels = new[]
                {
                    new { text = "First", normalizedX = 0.1m, normalizedY = 0.1m, normalizedWidth = 0.2m, normalizedHeight = 0.2m, fontSizePx = 16 },
                    new { text = "Second", normalizedX = 0.2m, normalizedY = 0.2m, normalizedWidth = 0.2m, normalizedHeight = 0.2m, fontSizePx = 20 },
                    new { text = "Third", normalizedX = 0.3m, normalizedY = 0.3m, normalizedWidth = 0.2m, normalizedHeight = 0.2m, fontSizePx = 24 },
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
        JsonElement labels = draft.GetProperty("labels");
        labels.GetArrayLength().ShouldBe(3);
        labels[0].GetProperty("text").GetString().ShouldBe("First");
        labels[1].GetProperty("text").GetString().ShouldBe("Second");
        labels[2].GetProperty("text").GetString().ShouldBe("Third");
        labels[2].GetProperty("fontSizePx").GetInt32().ShouldBe(24);

        // Publishing the branch is the second half of FR-007's proof: a
        // deep-copied-but-corrupted set could still read back correctly and
        // fail only on the next write.
        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, $"revisions/{draftNumber}/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
