using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Spec 150 (#2345), ADR-0164: a revision holds an ordered set of 1..8
/// labels, published atomically. T004-T006.
/// </summary>
public class OverlayLabelSetTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static Label MakeLabel(string text = "Label", decimal x = 0.1m) =>
        Label.From(text, NormalizedPosition.From(x, 0.1m), NormalizedSize.From(0.2m, 0.2m), 16);

    // --- Overlay.ValidateLabels (FR-001, FR-002, FR-003) ---

    [Fact]
    public void ValidateLabels_rejects_an_empty_set()
    {
        Option<LabelSetViolation> violation = Domain.Overlay.Overlay.ValidateLabels([]);

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(LabelSetViolation.Empty);
    }

    [Fact]
    public void ValidateLabels_accepts_a_set_at_exactly_MaxLabels()
    {
        List<Label> labels = Enumerable.Range(0, Label.MaxLabels)
            .Select(i => MakeLabel($"Label {i}"))
            .ToList();

        Option<LabelSetViolation> violation = Domain.Overlay.Overlay.ValidateLabels(labels);

        violation.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ValidateLabels_rejects_one_more_than_MaxLabels()
    {
        List<Label> labels = Enumerable.Range(0, Label.MaxLabels + 1)
            .Select(i => MakeLabel($"Label {i}"))
            .ToList();

        Option<LabelSetViolation> violation = Domain.Overlay.Overlay.ValidateLabels(labels);

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(LabelSetViolation.TooMany);
    }

    [Fact]
    public void ValidateLabels_accepts_a_single_label()
    {
        Option<LabelSetViolation> violation = Domain.Overlay.Overlay.ValidateLabels([MakeLabel()]);

        violation.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void CreateDraft_with_an_empty_label_set_throws_the_backstop()
    {
        OperatorIdentifier createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        Action act = () => Domain.Overlay.Overlay.CreateDraft(
            OverlayName.From("Empty"), [], createdBy, clock);

        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void CreateDraft_with_too_many_labels_throws_the_backstop()
    {
        OperatorIdentifier createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        List<Label> tooMany = Enumerable.Range(0, Label.MaxLabels + 1)
            .Select(i => MakeLabel($"Label {i}"))
            .ToList();

        Action act = () => Domain.Overlay.Overlay.CreateDraft(
            OverlayName.From("TooMany"), tooMany, createdBy, clock);

        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void EditDraft_with_an_empty_label_set_throws_the_backstop()
    {
        Domain.Overlay.Overlay overlay = new OverlayBuilder().Build();
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        Action act = () => overlay.EditDraft(OverlayRevisionNumber.One, [], clock);

        act.ShouldThrow<InvalidOperationException>();
    }

    // --- Ordered set, three labels (US1 happy path) ---

    [Fact]
    public void CreateDraft_with_three_labels_keeps_them_in_submission_order_with_dense_ordinals()
    {
        Label first = MakeLabel("First", 0.1m);
        Label second = MakeLabel("Second", 0.2m);
        Label third = MakeLabel("Third", 0.3m);
        OperatorIdentifier createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        Domain.Overlay.Overlay overlay = Domain.Overlay.Overlay.CreateDraft(
            OverlayName.From("Three Labels"), [first, second, third], createdBy, clock);

        Revision revision = overlay.Revisions.Single();
        revision.Labels.Count.ShouldBe(3);
        revision.Labels[0].Text.ShouldBe("First");
        revision.Labels[1].Text.ShouldBe("Second");
        revision.Labels[2].Text.ShouldBe("Third");
        revision.Labels[0].Ordinal.Value.ShouldBe(0);
        revision.Labels[1].Ordinal.Value.ShouldBe(1);
        revision.Labels[2].Ordinal.Value.ShouldBe(2);
    }

    [Fact]
    public void Publish_with_three_labels_raises_exactly_one_event_carrying_all_three()
    {
        Label first = MakeLabel("First", 0.1m);
        Label second = MakeLabel("Second", 0.2m);
        Label third = MakeLabel("Third", 0.3m);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabels([first, second, third]).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        overlay.Publish(OverlayRevisionNumber.One, by, clock);

        OverlayRevisionPublishedDomainEvent evt =
            overlay.PendingEvents.OfType<OverlayRevisionPublishedDomainEvent>().ShouldHaveSingleItem();
        evt.Labels.Count.ShouldBe(3);
        evt.Labels[0].Text.ShouldBe("First");
        evt.Labels[1].Text.ShouldBe("Second");
        evt.Labels[2].Text.ShouldBe("Third");
    }

    [Fact]
    public void ReplaceLabels_is_a_wholesale_replacement_not_a_merge()
    {
        Label first = MakeLabel("First", 0.1m);
        Label second = MakeLabel("Second", 0.2m);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabels([first, second]).Build();
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        Label replacement = MakeLabel("Replacement", 0.5m);

        overlay.EditDraft(OverlayRevisionNumber.One, [replacement], clock);

        Revision revision = overlay.Revisions.Single();
        revision.Labels.Count.ShouldBe(1);
        revision.Labels.Single().Text.ShouldBe("Replacement");
    }

    // --- FR-007: Branch deep-copies every label, including Position/Size ---

    [Fact]
    public void BranchDraft_deep_copies_every_label_in_the_set()
    {
        Label first = MakeLabel("First", 0.1m);
        Label second = MakeLabel("Second", 0.2m);
        Label third = MakeLabel("Third", 0.3m);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabels([first, second, third]).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        Revision published = overlay.Revisions.Single(r => r.Number == OverlayRevisionNumber.One);

        Revision draft = overlay.BranchDraft(by, clock);

        draft.Labels.Count.ShouldBe(3);
        for (int i = 0; i < 3; i++)
        {
            draft.Labels[i].ShouldBe(published.Labels[i]);
            ReferenceEquals(draft.Labels[i], published.Labels[i]).ShouldBeFalse();
            ReferenceEquals(draft.Labels[i].Position, published.Labels[i].Position).ShouldBeFalse();
            ReferenceEquals(draft.Labels[i].Size, published.Labels[i].Size).ShouldBeFalse();
        }
    }
}
