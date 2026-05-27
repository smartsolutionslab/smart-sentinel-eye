using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

public class OverlayRevisionStateMachineTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Draft_to_Published_is_allowed_and_raises_OverlayRevisionPublished()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        overlay.Publish(OverlayRevisionNumber.One, by, new OverlayBuilder.TestClock(FixedMoment));

        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Published);
        overlay.PendingEvents.OfType<OverlayRevisionPublishedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Draft_to_Archived_is_allowed_and_raises_no_event()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        overlay.ArchiveRevision(OverlayRevisionNumber.One, by, new OverlayBuilder.TestClock(FixedMoment));

        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Archived);
        // Drafts aren't observable to kiosks, so archiving one doesn't push.
        overlay.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Published_to_Draft_via_Revert_raises_Archived_for_kiosks()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        overlay.ClearPendingEvents();

        overlay.Revert(OverlayRevisionNumber.One, by, clock);

        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Draft);
        overlay.PendingEvents.OfType<OverlayRevisionArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Published_to_Archived_via_ArchiveRevision_raises_Archived()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        overlay.ClearPendingEvents();

        overlay.ArchiveRevision(OverlayRevisionNumber.One, by, clock);

        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Archived);
        overlay.PendingEvents.OfType<OverlayRevisionArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Archived_to_anything_else_is_forbidden()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.ArchiveRevision(OverlayRevisionNumber.One, by, clock);

        Action publish = () => overlay.Publish(OverlayRevisionNumber.One, by, clock);
        publish.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Re_archiving_an_Archived_revision_is_idempotent_and_silent()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.ArchiveRevision(OverlayRevisionNumber.One, by, clock);
        overlay.ClearPendingEvents();

        overlay.ArchiveRevision(OverlayRevisionNumber.One, by, clock);

        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Archived);
        overlay.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Editing_a_Published_revision_in_place_throws()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);

        Label newLabel = Label.From("Different", 0.1m, 0.1m, 0.2m, 0.2m, 20);
        Action act = () => overlay.EditDraft(OverlayRevisionNumber.One, newLabel, clock);
        act.ShouldThrow<InvalidOperationException>();
    }
}
