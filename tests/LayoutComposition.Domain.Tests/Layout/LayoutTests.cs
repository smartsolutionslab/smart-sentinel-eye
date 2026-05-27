using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class LayoutTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void CreateDraft_yields_revision_one_in_Draft_state_with_no_pending_events()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());

        Domain.Layout.Layout layout = new LayoutBuilder()
            .Named("Line-1 Entrance")
            .ForCamera(camera)
            .At(FixedMoment)
            .Build();

        layout.Name.Value.ShouldBe("Line-1 Entrance");
        layout.Revisions.Count.ShouldBe(1);
        Revision only = layout.Revisions[0];
        only.Number.ShouldBe(LayoutRevisionNumber.One);
        only.State.ShouldBe(LayoutRevisionState.Draft);
        only.Camera.ShouldBe(camera);
        only.PublishedAt.ShouldBeNull();
        only.ArchivedAt.ShouldBeNull();
        layout.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Publish_a_Draft_transitions_to_Published_and_raises_LayoutRevisionPublished()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Named("Line-1").Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        layout.Publish(LayoutRevisionNumber.One, by, clock);

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Published);
        layout.Revisions.Single().PublishedAt.ShouldBe(FixedMoment);

        LayoutRevisionPublishedDomainEvent evt =
            layout.PendingEvents.OfType<LayoutRevisionPublishedDomainEvent>().ShouldHaveSingleItem();
        evt.Layout.ShouldBe(layout.Id);
        evt.RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
        evt.PublishedBy.ShouldBe(by);
    }

    [Fact]
    public void BranchDraft_off_Published_yields_revision_two_in_Draft_with_the_same_camera()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().ForCamera(camera).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.ClearPendingEvents();

        Revision draft = layout.BranchDraft(by, clock);

        draft.Number.Value.ShouldBe(2);
        draft.State.ShouldBe(LayoutRevisionState.Draft);
        draft.Camera.ShouldBe(camera);
        layout.Revisions.Count.ShouldBe(2);
    }

    [Fact]
    public void BranchDraft_without_a_Published_revision_throws()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Action act = () => layout.BranchDraft(by, new LayoutBuilder.TestClock(FixedMoment));
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Publish_a_new_revision_atomically_archives_the_previous_Published()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        Revision draftTwo = layout.BranchDraft(by, clock);
        layout.ClearPendingEvents();

        layout.Publish(draftTwo.Number, by, clock);

        layout.Revisions.Count(r => r.State == LayoutRevisionState.Published).ShouldBe(1);
        layout.Revisions.Single(r => r.Number == LayoutRevisionNumber.One)
            .State.ShouldBe(LayoutRevisionState.Archived);
        layout.Revisions.Single(r => r.Number == draftTwo.Number)
            .State.ShouldBe(LayoutRevisionState.Published);

        layout.PendingEvents.OfType<LayoutRevisionArchivedDomainEvent>()
            .ShouldHaveSingleItem()
            .RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
        layout.PendingEvents.OfType<LayoutRevisionPublishedDomainEvent>()
            .ShouldHaveSingleItem()
            .RevisionNumber.ShouldBe(draftTwo.Number);
    }

    [Fact]
    public void At_most_one_revision_is_Published_after_any_sequence_of_operations()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        layout.Publish(LayoutRevisionNumber.One, by, clock);
        Revision two = layout.BranchDraft(by, clock);
        layout.Publish(two.Number, by, clock);
        Revision three = layout.BranchDraft(by, clock);
        layout.Publish(three.Number, by, clock);

        layout.Revisions.Count(r => r.State == LayoutRevisionState.Published).ShouldBe(1);
        layout.Revisions.Single(r => r.State == LayoutRevisionState.Published)
            .Number.ShouldBe(three.Number);
    }

    [Fact]
    public void EditDraft_in_place_does_not_spawn_a_new_revision()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        CameraIdentifier other = CameraIdentifier.From(Guid.CreateVersion7());

        layout.EditDraft(LayoutRevisionNumber.One, other, clock);

        layout.Revisions.Count.ShouldBe(1);
        layout.Revisions.Single().Camera.ShouldBe(other);
        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
    }

    [Fact]
    public void Revert_on_a_Published_revision_brings_it_back_to_Draft()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.ClearPendingEvents();

        layout.Revert(LayoutRevisionNumber.One, by, clock);

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
        layout.Revisions.Single().PublishedAt.ShouldBeNull();
        layout.PendingEvents.OfType<LayoutRevisionArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Publishing_a_missing_revision_number_throws()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Action act = () => layout.Publish(
            LayoutRevisionNumber.From(99), by, new LayoutBuilder.TestClock(FixedMoment));
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void CreateDraft_carries_the_optional_overlay_identifier()
    {
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().WithOverlay(overlay).Build();

        layout.Revisions.Single().Overlay.ShouldBe(overlay);
    }

    [Fact]
    public void AttachOverlay_on_a_Draft_revision_updates_the_binding()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        layout.AttachOverlay(LayoutRevisionNumber.One, overlay, clock);

        layout.Revisions.Single().Overlay.ShouldBe(overlay);
    }

    [Fact]
    public void AttachOverlay_with_null_clears_the_binding()
    {
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().WithOverlay(overlay).Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        layout.AttachOverlay(LayoutRevisionNumber.One, null, clock);

        layout.Revisions.Single().Overlay.ShouldBeNull();
    }

    [Fact]
    public void AttachOverlay_on_a_Published_revision_throws()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);

        Action act = () => layout.AttachOverlay(
            LayoutRevisionNumber.One, OverlayIdentifier.From(Guid.CreateVersion7()), clock);
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void BranchDraft_carries_the_overlay_from_the_Published_revision()
    {
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().WithOverlay(overlay).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);

        Revision draft = layout.BranchDraft(by, clock);

        draft.Overlay.ShouldBe(overlay);
    }
}
