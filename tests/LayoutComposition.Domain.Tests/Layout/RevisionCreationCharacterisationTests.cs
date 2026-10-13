using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

/// <summary>
/// Characterisation (spec 345, ADR-0144): pins what a revision's
/// <see cref="Creation"/> is and, as importantly, that it is a distinct
/// instance. <c>Creation</c> is an EF owned type on both <c>layouts</c> and
/// <c>layout_revisions</c>; one instance under two owners throws on save, and
/// no unit test elsewhere would notice a refactor that shared it.
/// </summary>
public class RevisionCreationCharacterisationTests
{
    private static readonly DateTimeOffset CreatedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset BranchedMoment =
        DateTimeOffset.Parse("2026-05-27T14:30:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void A_new_layouts_first_revision_creation_is_not_the_same_instance_as_the_layouts_creation()
    {
        var layout = new LayoutBuilder().At(CreatedMoment).Build();

        layout.Revisions[0].Creation.ShouldNotBeSameAs(layout.Creation);
    }

    [Fact]
    public void A_new_layouts_first_revision_creation_equals_the_layouts_creation_by_value()
    {
        var layout = new LayoutBuilder().At(CreatedMoment).Build();

        layout.Revisions[0].Creation.ShouldBe(layout.Creation);
    }

    [Fact]
    public void A_branched_revision_creation_is_the_branch_time_and_the_branching_operator()
    {
        var layout = new LayoutBuilder().At(CreatedMoment).Build();
        var author = layout.Revisions[0].Creation.By;
        var brancher = OperatorIdentifier.From(Guid.CreateVersion7());
        brancher.ShouldNotBe(author);
        layout.Publish(LayoutRevisionNumber.One, author, new LayoutBuilder.TestClock(CreatedMoment));

        var branched = layout.BranchDraft(brancher, new LayoutBuilder.TestClock(BranchedMoment));

        branched.Creation.At.Value.ShouldBe(BranchedMoment);
        branched.Creation.By.ShouldBe(brancher);
    }

    [Fact]
    public void A_branched_revision_creation_is_not_the_same_instance_as_the_base_revisions_or_the_layouts()
    {
        var layout = new LayoutBuilder().At(CreatedMoment).Build();
        var author = layout.Revisions[0].Creation.By;
        layout.Publish(LayoutRevisionNumber.One, author, new LayoutBuilder.TestClock(CreatedMoment));
        var baseRevision = layout.Revisions[0];

        var branched = layout.BranchDraft(author, new LayoutBuilder.TestClock(BranchedMoment));

        branched.Creation.ShouldNotBeSameAs(baseRevision.Creation);
        branched.Creation.ShouldNotBeSameAs(layout.Creation);
    }

    [Fact]
    public void A_branched_revision_leaves_the_base_revision_and_the_layout_creation_untouched()
    {
        var layout = new LayoutBuilder().At(CreatedMoment).Build();
        var author = layout.Revisions[0].Creation.By;
        layout.Publish(LayoutRevisionNumber.One, author, new LayoutBuilder.TestClock(CreatedMoment));

        layout.BranchDraft(
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(BranchedMoment));

        layout.Revisions[0].Creation.At.Value.ShouldBe(CreatedMoment);
        layout.Revisions[0].Creation.By.ShouldBe(author);
        layout.Creation.At.Value.ShouldBe(CreatedMoment);
        layout.Creation.By.ShouldBe(author);
    }
}
