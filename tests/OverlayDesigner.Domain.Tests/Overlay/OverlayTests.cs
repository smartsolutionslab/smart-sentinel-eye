using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

public class OverlayTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void CreateDraft_records_when_it_happened_and_who_did_it_on_the_chain_and_its_revision()
    {
        OperatorIdentifier createdBy = OperatorIdentifier.From(Guid.CreateVersion7());

        Domain.Overlay.Overlay overlay = new OverlayBuilder()
            .At(FixedMoment)
            .CreatedBy(createdBy)
            .Build();

        overlay.Creation.At.Value.ShouldBe(FixedMoment);
        overlay.Creation.By.ShouldBe(createdBy);
        overlay.Revisions[0].Creation.At.Value.ShouldBe(FixedMoment);
        overlay.Revisions[0].Creation.By.ShouldBe(createdBy);
    }

    [Fact]
    public void CreateDraft_yields_revision_one_in_Draft_with_no_pending_events()
    {
        Label label = Label.From("Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48);

        Domain.Overlay.Overlay overlay = new OverlayBuilder()
            .Named("Line-1 Title")
            .WithLabel(label)
            .At(FixedMoment)
            .Build();

        overlay.Name.Value.ShouldBe("Line-1 Title");
        overlay.Revisions.Count.ShouldBe(1);
        Revision only = overlay.Revisions[0];
        only.Number.ShouldBe(OverlayRevisionNumber.One);
        only.State.ShouldBe(OverlayRevisionState.Draft);
        only.Label.ShouldBe(label);
        only.PublishedAt.ShouldBeNull();
        only.ArchivedAt.ShouldBeNull();
        overlay.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Publish_a_Draft_raises_OverlayRevisionPublished_with_the_Label()
    {
        Label label = Label.From("Hello", 0.2m, 0.3m, 0.4m, 0.5m, 32);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabel(label).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        overlay.Publish(OverlayRevisionNumber.One, by, clock);

        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Published);
        overlay.Revisions.Single().PublishedAt!.Value.ShouldBe(FixedMoment);

        OverlayRevisionPublishedDomainEvent evt =
            overlay.PendingEvents.OfType<OverlayRevisionPublishedDomainEvent>().ShouldHaveSingleItem();
        evt.Overlay.ShouldBe(overlay.Id);
        evt.RevisionNumber.ShouldBe(OverlayRevisionNumber.One);
        evt.Label.ShouldBe(label);
        evt.PublishedBy.ShouldBe(by);
    }

    [Fact]
    public void BranchDraft_off_Published_yields_revision_two_with_the_same_label()
    {
        Label label = Label.From("Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabel(label).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        overlay.ClearPendingEvents();

        Revision draft = overlay.BranchDraft(by, clock);

        draft.Number.Value.ShouldBe(2);
        draft.State.ShouldBe(OverlayRevisionState.Draft);
        draft.Label.ShouldBe(label);
        overlay.Revisions.Count.ShouldBe(2);
    }

    [Fact]
    public void BranchDraft_gives_the_new_revision_its_own_label_instance()
    {
        // The branched revision must not share the base revision's Label
        // object: Label is an EF-owned entity keyed on its owner, so a shared
        // instance breaks owned-entity fixup on save (issue #955). Value-equal,
        // reference-distinct.
        Label label = Label.From("Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabel(label).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        Revision published = overlay.Revisions.Single(r => r.Number == OverlayRevisionNumber.One);

        Revision draft = overlay.BranchDraft(by, clock);

        draft.Label.ShouldBe(published.Label);
        ReferenceEquals(draft.Label, published.Label).ShouldBeFalse();
    }

    [Fact]
    public void BranchDraft_without_a_Published_revision_throws()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Action act = () => overlay.BranchDraft(by, new OverlayBuilder.TestClock(FixedMoment));
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Publish_a_new_revision_atomically_archives_the_previous_Published()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        Revision draftTwo = overlay.BranchDraft(by, clock);
        overlay.ClearPendingEvents();

        overlay.Publish(draftTwo.Number, by, clock);

        overlay.Revisions.Count(r => r.State == OverlayRevisionState.Published).ShouldBe(1);
        overlay.Revisions.Single(r => r.Number == OverlayRevisionNumber.One)
            .State.ShouldBe(OverlayRevisionState.Archived);
        overlay.Revisions.Single(r => r.Number == draftTwo.Number)
            .State.ShouldBe(OverlayRevisionState.Published);

        overlay.PendingEvents.OfType<OverlayRevisionArchivedDomainEvent>()
            .ShouldHaveSingleItem()
            .RevisionNumber.ShouldBe(OverlayRevisionNumber.One);
        overlay.PendingEvents.OfType<OverlayRevisionPublishedDomainEvent>()
            .ShouldHaveSingleItem()
            .RevisionNumber.ShouldBe(draftTwo.Number);
    }

    [Fact]
    public void At_most_one_revision_is_Published_after_any_sequence()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        Revision two = overlay.BranchDraft(by, clock);
        overlay.Publish(two.Number, by, clock);
        Revision three = overlay.BranchDraft(by, clock);
        overlay.Publish(three.Number, by, clock);

        overlay.Revisions.Count(r => r.State == OverlayRevisionState.Published).ShouldBe(1);
        overlay.Revisions.Single(r => r.State == OverlayRevisionState.Published)
            .Number.ShouldBe(three.Number);
    }

    [Fact]
    public void EditDraft_in_place_does_not_spawn_a_new_revision()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        Label newLabel = Label.From("Updated", 0.1m, 0.2m, 0.3m, 0.4m, 20);

        overlay.EditDraft(OverlayRevisionNumber.One, newLabel, clock);

        overlay.Revisions.Count.ShouldBe(1);
        overlay.Revisions.Single().Label.ShouldBe(newLabel);
        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Draft);
    }

    [Fact]
    public void Revert_on_a_Published_revision_brings_it_back_to_Draft()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        overlay.ClearPendingEvents();

        overlay.Revert(OverlayRevisionNumber.One, by, clock);

        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Draft);
        overlay.Revisions.Single().PublishedAt.ShouldBeNull();
        overlay.PendingEvents.OfType<OverlayRevisionArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Publishing_a_missing_revision_number_throws()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Action act = () => overlay.Publish(
            OverlayRevisionNumber.From(99), by, new OverlayBuilder.TestClock(FixedMoment));
        act.ShouldThrow<InvalidOperationException>();
    }

    /// <summary>
    /// Spec 037 FR-001/FR-002 (ADR-0121). Asserts the PAYLOAD, not that a draft
    /// appeared. A fallback that branched an empty draft would satisfy any
    /// assertion about a draft existing while recovering nothing — and the label
    /// is the entire reason recovery beats recreating the overlay by hand, which
    /// already works because a stranded chain releases its name.
    ///
    /// <para>
    /// The geometry is asserted alongside the text: a copy that carried the
    /// words but reset the position or the font size would put the label
    /// somewhere the operator never chose, on a kiosk, without warning.
    /// </para>
    /// </summary>
    [Fact]
    public void BranchDraft_on_a_fully_archived_chain_recovers_the_label()
    {
        Label label = Label.From("Rolling Mill A", 0.25m, 0.8m, 0.4m, 0.1m, 64);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabel(label).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        overlay.ArchiveRevision(OverlayRevisionNumber.One, by, clock);
        overlay.ClearPendingEvents();

        Revision recovered = overlay.BranchDraft(by, clock);

        recovered.Number.Value.ShouldBe(2);
        recovered.State.ShouldBe(OverlayRevisionState.Draft);
        recovered.Label.Text.ShouldBe("Rolling Mill A");
        recovered.Label.NormalizedX.ShouldBe(0.25m);
        recovered.Label.NormalizedY.ShouldBe(0.8m);
        recovered.Label.NormalizedWidth.ShouldBe(0.4m);
        recovered.Label.NormalizedHeight.ShouldBe(0.1m);
        recovered.Label.FontSizePx.ShouldBe(64);
    }

    /// <summary>
    /// Spec 037 FR-004. Branching alone is not recovery — a draft nobody can
    /// publish leaves the overlay exactly as unusable as before while passing
    /// every assertion in the test above.
    /// </summary>
    [Fact]
    public void A_recovered_draft_can_be_edited_and_published()
    {
        Label replacement = Label.From("Rolling Mill B", 0.5m, 0.05m, 0.3m, 0.08m, 48);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        overlay.ArchiveRevision(OverlayRevisionNumber.One, by, clock);

        Revision recovered = overlay.BranchDraft(by, clock);
        overlay.EditDraft(recovered.Number, replacement, clock);
        overlay.Publish(recovered.Number, by, clock);

        recovered.State.ShouldBe(OverlayRevisionState.Published);
        recovered.Label.Text.ShouldBe("Rolling Mill B");
    }

    /// <summary>
    /// Spec 037 FR-008. Asserts WHICH revision was copied, not that the branch
    /// succeeded — the two are indistinguishable on success alone, and this is
    /// the case a widened "newest revision, whatever its state" fallback breaks
    /// in the opposite direction: it would copy the abandoned draft instead of
    /// the live published overlay.
    /// </summary>
    [Fact]
    public void BranchDraft_prefers_the_Published_revision_over_an_archived_newer_one()
    {
        Label live = Label.From("Live", 0.5m, 0.05m, 0.3m, 0.08m, 48);
        Label abandoned = Label.From("Abandoned", 0.1m, 0.1m, 0.2m, 0.05m, 32);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabel(live).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);

        Revision draftTwo = overlay.BranchDraft(by, clock);
        overlay.EditDraft(draftTwo.Number, abandoned, clock);
        overlay.ArchiveRevision(draftTwo.Number, by, clock);

        Revision draftThree = overlay.BranchDraft(by, clock);

        draftThree.Number.Value.ShouldBe(3);
        draftThree.Label.Text.ShouldBe("Live");
    }
}
