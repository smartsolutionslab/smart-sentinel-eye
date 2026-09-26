using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall;

/// <summary>
/// Spec 258 plan.md §2: <c>SceneTarget</c> is a discriminated VO with two
/// variants, <c>Next</c> and <c>Layout(LayoutIdentifier)</c> — the shape a
/// switch request (manual or, in US2, rule-driven) is parsed into at the API
/// edge (plan.md §4.4). Mirrors the record-equality coverage style
/// <c>RuleAction</c>'s variants would get.
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
        LayoutIdentifier target = LayoutIdentifier.New();
        SceneTarget.Layout layout = new(target);

        layout.Value.ShouldBe(target);
    }

    [Fact]
    public void Layout_instances_with_the_same_identifier_are_equal()
    {
        LayoutIdentifier target = LayoutIdentifier.New();
        SceneTarget.Layout a = new(target);
        SceneTarget.Layout b = new(target);

        a.ShouldBe(b);
    }

    [Fact]
    public void Layout_instances_with_different_identifiers_are_not_equal()
    {
        SceneTarget.Layout a = new(LayoutIdentifier.New());
        SceneTarget.Layout b = new(LayoutIdentifier.New());

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Next_and_Layout_are_never_equal_to_each_other()
    {
        SceneTarget next = new SceneTarget.Next();
        SceneTarget layout = new SceneTarget.Layout(LayoutIdentifier.New());

        next.ShouldNotBe(layout);
    }
}
