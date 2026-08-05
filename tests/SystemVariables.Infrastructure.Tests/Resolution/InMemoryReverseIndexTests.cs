using SmartSentinelEye.SystemVariables.Infrastructure.Resolution;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Tests.Resolution;

/// <summary>
/// The shipped reverse index, tested directly (spec 014 T003, closes #461).
///
/// <para>
/// Until now the only <c>InMemoryReverseIndex</c> under <c>tests/</c> was a
/// hand-written double in the Application test project, and nothing referenced
/// the class that actually runs. Two implementations kept in step by hand is
/// how the one that ships quietly diverges — the same finding the #1299 review
/// raised against <c>InMemoryRuleCache</c>.
/// </para>
///
/// <para>
/// This exists before spec 014 changes the key (T033). Adding the fab first
/// and the tests afterwards would mean asserting "the fab keying works"
/// against a copy the change also has to be applied to.
/// </para>
/// </summary>
public class InMemoryReverseIndexTests
{
    private static string Label(params string[] names) =>
        string.Join(" and ", names.Select(name => $"{{{{{name}}}}}"));

    // ---- lookup ----

    [Fact]
    public void An_overlay_is_found_by_every_variable_its_label_references()
    {
        InMemoryReverseIndex index = new();
        Guid overlay = Guid.CreateVersion7();

        index.UpsertOverlayReferences(overlay, Label("oeeLine1", "cycleTime"));

        index.LookupOverlays("oeeLine1").ShouldBe([overlay]);
        index.LookupOverlays("cycleTime").ShouldBe([overlay]);
    }

    [Fact]
    public void A_variable_nothing_references_returns_empty_rather_than_throwing()
    {
        InMemoryReverseIndex index = new();

        index.LookupOverlays("neverReferenced").ShouldBeEmpty();
    }

    [Fact]
    public void Several_overlays_referencing_one_variable_are_all_returned()
    {
        InMemoryReverseIndex index = new();
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();

        index.UpsertOverlayReferences(first, Label("oeeLine1"));
        index.UpsertOverlayReferences(second, Label("oeeLine1"));

        index.LookupOverlays("oeeLine1").ShouldBe([first, second], ignoreOrder: true);
    }

    // ---- re-publishing ----

    [Fact]
    public void Re_publishing_with_a_different_variable_drops_the_old_reference()
    {
        // The case a naive "add what is referenced" implementation gets wrong:
        // the overlay no longer mentions oeeLine1, so a change to oeeLine1 must
        // stop reaching it. Leaving the stale entry means the screen updates
        // for a variable it no longer shows.
        InMemoryReverseIndex index = new();
        Guid overlay = Guid.CreateVersion7();

        index.UpsertOverlayReferences(overlay, Label("oeeLine1"));
        index.UpsertOverlayReferences(overlay, Label("cycleTime"));

        index.LookupOverlays("oeeLine1").ShouldBeEmpty();
        index.LookupOverlays("cycleTime").ShouldBe([overlay]);
    }

    [Fact]
    public void Re_publishing_does_not_duplicate_an_overlay_against_the_same_variable()
    {
        InMemoryReverseIndex index = new();
        Guid overlay = Guid.CreateVersion7();

        index.UpsertOverlayReferences(overlay, Label("oeeLine1"));
        index.UpsertOverlayReferences(overlay, Label("oeeLine1"));

        index.LookupOverlays("oeeLine1").Count.ShouldBe(1);
    }

    [Fact]
    public void Re_publishing_one_overlay_leaves_another_referencing_the_same_variable()
    {
        InMemoryReverseIndex index = new();
        Guid kept = Guid.CreateVersion7();
        Guid changed = Guid.CreateVersion7();

        index.UpsertOverlayReferences(kept, Label("oeeLine1"));
        index.UpsertOverlayReferences(changed, Label("oeeLine1"));
        index.UpsertOverlayReferences(changed, Label("cycleTime"));

        index.LookupOverlays("oeeLine1").ShouldBe([kept]);
    }

    // ---- removal ----

    [Fact]
    public void Removing_an_overlay_removes_it_from_every_variable_and_from_the_label_store()
    {
        InMemoryReverseIndex index = new();
        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, Label("oeeLine1", "cycleTime"));

        index.RemoveOverlay(overlay);

        index.LookupOverlays("oeeLine1").ShouldBeEmpty();
        index.LookupOverlays("cycleTime").ShouldBeEmpty();
        index.LookupLabelText(overlay).ShouldBeNull();
        index.AllOverlays().ShouldNotContain(overlay);
    }

    [Fact]
    public void Removing_an_overlay_that_was_never_indexed_is_a_no_op()
    {
        InMemoryReverseIndex index = new();
        Guid present = Guid.CreateVersion7();
        index.UpsertOverlayReferences(present, Label("oeeLine1"));

        index.RemoveOverlay(Guid.CreateVersion7());

        index.LookupOverlays("oeeLine1").ShouldBe([present]);
    }

    // ---- label text ----

    [Fact]
    public void The_label_text_round_trips_and_the_latest_wins()
    {
        InMemoryReverseIndex index = new();
        Guid overlay = Guid.CreateVersion7();

        index.UpsertOverlayReferences(overlay, "first {{oeeLine1}}");
        index.UpsertOverlayReferences(overlay, "second {{cycleTime}}");

        index.LookupLabelText(overlay).ShouldBe("second {{cycleTime}}");
    }

    [Fact]
    public void An_unknown_overlay_has_no_label_text()
    {
        new InMemoryReverseIndex().LookupLabelText(Guid.CreateVersion7()).ShouldBeNull();
    }

    [Fact]
    public void A_label_referencing_nothing_is_still_indexed_as_an_overlay()
    {
        // It has no variables, so it appears in no lookup — but it exists, and
        // AllOverlays is what the seeder reconciles against.
        InMemoryReverseIndex index = new();
        Guid overlay = Guid.CreateVersion7();

        index.UpsertOverlayReferences(overlay, "no placeholders here");

        index.AllOverlays().ShouldBe([overlay]);
        index.LookupLabelText(overlay).ShouldBe("no placeholders here");
    }

    // ---- versions ----

    [Fact]
    public void The_version_starts_at_zero_and_increments_per_overlay_independently()
    {
        InMemoryReverseIndex index = new();
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();

        index.CurrentVersionFor(first).ShouldBe(0);

        index.NextVersionFor(first).ShouldBe(1);
        index.NextVersionFor(first).ShouldBe(2);
        index.NextVersionFor(second).ShouldBe(1);

        index.CurrentVersionFor(first).ShouldBe(2);
        index.CurrentVersionFor(second).ShouldBe(1);
    }

    // ---- concurrency ----

    [Fact]
    public async Task Concurrent_upserts_and_lookups_do_not_corrupt_the_index()
    {
        // The index is a process-wide singleton read on the resolution path
        // while overlay publishes write to it. The per-value `lock` inside the
        // implementation is what makes that safe, and nothing else asserts it.
        InMemoryReverseIndex index = new();
        Guid[] overlays = [.. Enumerable.Range(0, 40).Select(_ => Guid.CreateVersion7())];

        await Task.WhenAll(overlays.Select(overlay => Task.Run(() =>
        {
            index.UpsertOverlayReferences(overlay, Label("shared"));
            index.LookupOverlays("shared");
            index.NextVersionFor(overlay);
        })));

        index.LookupOverlays("shared").ShouldBe(overlays, ignoreOrder: true);
        index.AllOverlays().Count.ShouldBe(overlays.Length);
    }

    [Fact]
    public async Task Concurrent_removal_while_reading_leaves_a_consistent_view()
    {
        InMemoryReverseIndex index = new();
        Guid[] overlays = [.. Enumerable.Range(0, 40).Select(_ => Guid.CreateVersion7())];
        foreach (Guid overlay in overlays)
        {
            index.UpsertOverlayReferences(overlay, Label("shared"));
        }

        await Task.WhenAll(overlays.Select(overlay => Task.Run(() =>
        {
            index.LookupOverlays("shared");
            index.RemoveOverlay(overlay);
        })));

        index.LookupOverlays("shared").ShouldBeEmpty();
        index.AllOverlays().ShouldBeEmpty();
    }
}
