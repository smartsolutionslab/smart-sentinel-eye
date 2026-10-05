using System.Globalization;
using System.Reflection;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Spec 150 (#2345), ADR-0164 and spec 300 (#2349), ADR-0165: a revision
/// holds an ordered set of 1..8 elements, published atomically, and the cap
/// counts every kind, not only text. Merged from <c>OverlayLabelSetTests</c>
/// (spec 150, T004's behaviour-preserving rename) — the Empty/TooMany-at-
/// the-old-boundary/single-element cases below are that rename's territory,
/// unchanged in value; <see cref="ValidateElements_rejects_five_boxes_and_four_texts_as_TooMany"/>
/// is spec 300's new cross-kind case.
/// </summary>
public class OverlayElementSetTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static OverlayElement MakeLabel(string text = "Label", decimal x = 0.1m) =>
        OverlayElement.TextElement(text, 16, NormalizedPosition.From(x, 0.1m), NormalizedSize.From(0.2m, 0.2m), OverlayColor.Default);

    [Fact]
    public void ValidateElements_rejects_five_boxes_and_four_texts_as_TooMany()
    {
        OverlayColor color = OverlayColor.From("#d32f2f");
        OverlayColor textColor = OverlayColor.From("#FFFFFFD9");
        NormalizedSize size = NormalizedSize.From(0.2m, 0.2m);
        List<OverlayElement> elements = [
            OverlayElement.Box(NormalizedPosition.From(0.0m, 0.1m), size, color),
            OverlayElement.Box(NormalizedPosition.From(0.1m, 0.1m), size, color),
            OverlayElement.Box(NormalizedPosition.From(0.2m, 0.1m), size, color),
            OverlayElement.Box(NormalizedPosition.From(0.3m, 0.1m), size, color),
            OverlayElement.Box(NormalizedPosition.From(0.4m, 0.1m), size, color),
            OverlayElement.TextElement("Label", 16, NormalizedPosition.From(0.5m, 0.1m), size, textColor),
            OverlayElement.TextElement("Label", 16, NormalizedPosition.From(0.6m, 0.1m), size, textColor),
            OverlayElement.TextElement("Label", 16, NormalizedPosition.From(0.7m, 0.1m), size, textColor),
            OverlayElement.TextElement("Label", 16, NormalizedPosition.From(0.8m, 0.1m), size, textColor),
        ];

        Option<ElementSetViolation> violation = Domain.Overlay.Overlay.ValidateElements(elements);

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(ElementSetViolation.TooMany);
    }

    // --- Overlay.ValidateElements (FR-001, FR-002, FR-003) ---

    [Fact]
    public void ValidateElements_rejects_an_empty_set()
    {
        Option<ElementSetViolation> violation = Domain.Overlay.Overlay.ValidateElements([]);

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(ElementSetViolation.Empty);
    }

    [Fact]
    public void ValidateElements_accepts_a_set_at_exactly_MaxElements()
    {
        List<OverlayElement> labels = Enumerable.Range(0, OverlayElement.MaxElements)
            .Select(i => MakeLabel($"Label {i}"))
            .ToList();

        Option<ElementSetViolation> violation = Domain.Overlay.Overlay.ValidateElements(labels);

        violation.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ValidateElements_rejects_one_more_than_MaxElements()
    {
        List<OverlayElement> labels = Enumerable.Range(0, OverlayElement.MaxElements + 1)
            .Select(i => MakeLabel($"Label {i}"))
            .ToList();

        Option<ElementSetViolation> violation = Domain.Overlay.Overlay.ValidateElements(labels);

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(ElementSetViolation.TooMany);
    }

    [Fact]
    public void ValidateElements_accepts_a_single_label()
    {
        Option<ElementSetViolation> violation = Domain.Overlay.Overlay.ValidateElements([MakeLabel()]);

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
        List<OverlayElement> tooMany = Enumerable.Range(0, OverlayElement.MaxElements + 1)
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
        OverlayElement first = MakeLabel("First", 0.1m);
        OverlayElement second = MakeLabel("Second", 0.2m);
        OverlayElement third = MakeLabel("Third", 0.3m);
        OperatorIdentifier createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        Domain.Overlay.Overlay overlay = Domain.Overlay.Overlay.CreateDraft(
            OverlayName.From("Three Labels"), [first, second, third], createdBy, clock);

        Revision revision = overlay.Revisions.Single();
        revision.Elements.Count.ShouldBe(3);
        revision.Elements[0].Text!.Value.ShouldBe("First");
        revision.Elements[1].Text!.Value.ShouldBe("Second");
        revision.Elements[2].Text!.Value.ShouldBe("Third");
        revision.Elements[0].Ordinal.Value.ShouldBe(0);
        revision.Elements[1].Ordinal.Value.ShouldBe(1);
        revision.Elements[2].Ordinal.Value.ShouldBe(2);
    }

    [Fact]
    public void Publish_with_three_labels_raises_exactly_one_event_carrying_all_three()
    {
        OverlayElement first = MakeLabel("First", 0.1m);
        OverlayElement second = MakeLabel("Second", 0.2m);
        OverlayElement third = MakeLabel("Third", 0.3m);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabels([first, second, third]).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);

        overlay.Publish(OverlayRevisionNumber.One, by, clock);

        OverlayRevisionPublishedDomainEvent evt =
            overlay.PendingEvents.OfType<OverlayRevisionPublishedDomainEvent>().ShouldHaveSingleItem();
        evt.Elements.Count.ShouldBe(3);
        evt.Elements[0].Text!.Value.ShouldBe("First");
        evt.Elements[1].Text!.Value.ShouldBe("Second");
        evt.Elements[2].Text!.Value.ShouldBe("Third");
    }

    [Fact]
    public void ReplaceLabels_is_a_wholesale_replacement_not_a_merge()
    {
        OverlayElement first = MakeLabel("First", 0.1m);
        OverlayElement second = MakeLabel("Second", 0.2m);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabels([first, second]).Build();
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        OverlayElement replacement = MakeLabel("Replacement", 0.5m);

        overlay.EditDraft(OverlayRevisionNumber.One, [replacement], clock);

        Revision revision = overlay.Revisions.Single();
        revision.Elements.Count.ShouldBe(1);
        revision.Elements.Single().Text!.Value.ShouldBe("Replacement");
    }

    // --- FR-007: Branch deep-copies every label, including Position/Size ---

    [Fact]
    public void BranchDraft_deep_copies_every_label_in_the_set()
    {
        OverlayElement first = MakeLabel("First", 0.1m);
        OverlayElement second = MakeLabel("Second", 0.2m);
        OverlayElement third = MakeLabel("Third", 0.3m);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabels([first, second, third]).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new OverlayBuilder.TestClock(FixedMoment);
        overlay.Publish(OverlayRevisionNumber.One, by, clock);
        Revision published = overlay.Revisions.Single(r => r.Number == OverlayRevisionNumber.One);

        Revision draft = overlay.BranchDraft(by, clock);

        draft.Elements.Count.ShouldBe(3);
        for (int i = 0; i < 3; i++)
        {
            draft.Elements[i].ShouldBe(published.Elements[i]);
            ReferenceEquals(draft.Elements[i], published.Elements[i]).ShouldBeFalse();
            ReferenceEquals(draft.Elements[i].Position, published.Elements[i].Position).ShouldBeFalse();
            ReferenceEquals(draft.Elements[i].Size, published.Elements[i].Size).ShouldBeFalse();
        }
    }

    // --- Phase-6 should-fix: the sort itself is a guarded contract, not an
    // unverified claim (spec 150, #2345). The integration-test counterfactual
    // in OverlayRevisionLifecycleIntegrationTests could not reproduce a
    // scramble through this app's own EF/Postgres query shape (reported
    // honestly there, not assumed). This test guards the contract directly,
    // with no EF or Postgres involved: it reaches past the public API via
    // reflection on Revision's private backing list — the one way, short of
    // adding InternalsVisibleTo, to get a revision's labels into a physical
    // order that disagrees with their own ordinals — and checks that
    // `Revision.Elements` still returns them sorted. Reflection is the test
    // tool here specifically because production code has no way to create
    // this state; that is the point.

    [Fact]
    public void Labels_returns_sorted_order_even_when_the_backing_list_is_physically_reversed()
    {
        OverlayElement first = MakeLabel("First", 0.1m);
        OverlayElement second = MakeLabel("Second", 0.2m);
        OverlayElement third = MakeLabel("Third", 0.3m);
        Domain.Overlay.Overlay overlay = new OverlayBuilder().WithLabels([first, second, third]).Build();
        Revision revision = overlay.Revisions.Single();
        revision.Elements.Select(label => label.Text!.Value).ShouldBe(["First", "Second", "Third"]);

        FieldInfo backingField = typeof(Revision).GetField("elements", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Revision's private 'elements' field was not found — rename tracked here must be updated.");
        List<OverlayElement> backingList = (List<OverlayElement>)backingField.GetValue(revision)!;
        backingList.Reverse();

        // The physical order is now reversed; each label's own Ordinal is untouched.
        backingList.Select(label => label.Text!.Value).ShouldBe(["Third", "Second", "First"]);
        backingList.Select(label => label.Ordinal.Value).ShouldBe([2, 1, 0]);

        revision.Elements.Select(label => label.Text!.Value).ShouldBe(["First", "Second", "Third"]);
    }
}
