using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

/// <summary>
/// Spec 296 plan.md §2.1: Automation's own <c>SceneTarget</c> — a
/// discriminated VO with two variants, <c>Next</c> and
/// <c>Layout(LayoutIdentifier)</c>, deliberately separate from
/// <c>LayoutComposition.Domain.Wall.SceneTarget</c> (§III). Mirrors
/// <c>LayoutComposition.Domain.Tests.Wall.SceneTargetTests</c>.
/// </summary>
public class SceneTargetTests
{
    [Fact]
    public void Next_instances_are_equal_regardless_of_construction_site()
    {
        SceneTarget.Next a = new();
        SceneTarget.Next b = new();

        a.ShouldBe(b);
        ((SceneTarget)a).ShouldBe(b);
    }

    [Fact]
    public void Layout_carries_its_target_identifier()
    {
        LayoutIdentifier target = LayoutIdentifier.From(Guid.CreateVersion7());
        SceneTarget.Layout layout = new(target);

        layout.Value.ShouldBe(target);
    }

    [Fact]
    public void Layout_instances_with_the_same_identifier_are_equal()
    {
        LayoutIdentifier target = LayoutIdentifier.From(Guid.CreateVersion7());
        SceneTarget.Layout a = new(target);
        SceneTarget.Layout b = new(target);

        a.ShouldBe(b);
    }

    [Fact]
    public void Layout_instances_with_different_identifiers_are_not_equal()
    {
        SceneTarget.Layout a = new(LayoutIdentifier.From(Guid.CreateVersion7()));
        SceneTarget.Layout b = new(LayoutIdentifier.From(Guid.CreateVersion7()));

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Next_and_Layout_are_never_equal_to_each_other()
    {
        SceneTarget next = new SceneTarget.Next();
        SceneTarget layout = new SceneTarget.Layout(LayoutIdentifier.From(Guid.CreateVersion7()));

        next.ShouldNotBe(layout);
    }
}
