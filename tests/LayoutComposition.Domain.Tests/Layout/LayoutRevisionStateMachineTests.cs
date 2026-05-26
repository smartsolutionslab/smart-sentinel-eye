using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class LayoutRevisionStateMachineTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Draft_to_Published_is_allowed_and_raises_LayoutRevisionPublished()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        layout.Publish(LayoutRevisionNumber.One, by, new LayoutBuilder.TestClock(FixedMoment));

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Published);
        layout.PendingEvents.OfType<LayoutRevisionPublishedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Draft_to_Archived_is_allowed_via_ArchiveRevision_and_raises_no_event()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        layout.ArchiveRevision(LayoutRevisionNumber.One, by, new LayoutBuilder.TestClock(FixedMoment));

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Archived);
        // Drafts aren't kiosk-observable, so archiving one doesn't push to kiosks.
        layout.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Published_to_Draft_is_allowed_via_Revert_and_raises_Archived_for_kiosks()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.ClearPendingEvents();

        layout.Revert(LayoutRevisionNumber.One, by, clock);

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
        layout.PendingEvents.OfType<LayoutRevisionArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Published_to_Archived_is_allowed_via_ArchiveRevision_and_raises_Archived()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.ClearPendingEvents();

        layout.ArchiveRevision(LayoutRevisionNumber.One, by, clock);

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Archived);
        LayoutRevisionArchivedDomainEvent evt =
            layout.PendingEvents.OfType<LayoutRevisionArchivedDomainEvent>().ShouldHaveSingleItem();
        evt.RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
    }

    [Fact]
    public void Archived_to_anything_else_is_forbidden()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.ArchiveRevision(LayoutRevisionNumber.One, by, clock);

        Action publish = () => layout.Publish(LayoutRevisionNumber.One, by, clock);
        publish.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Re_archiving_an_already_archived_revision_is_idempotent_and_silent()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.ArchiveRevision(LayoutRevisionNumber.One, by, clock);
        layout.ClearPendingEvents();

        layout.ArchiveRevision(LayoutRevisionNumber.One, by, clock);

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Archived);
        layout.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Editing_a_Published_revision_in_place_is_forbidden()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);

        CameraIdentifier other = CameraIdentifier.From(Guid.CreateVersion7());
        Action act = () => layout.EditDraft(LayoutRevisionNumber.One, other, clock);
        act.ShouldThrow<InvalidOperationException>();
    }
}
