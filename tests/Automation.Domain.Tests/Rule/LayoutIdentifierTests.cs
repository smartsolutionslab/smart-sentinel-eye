using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

/// <summary>
/// Spec 296 plan.md §2.1: <c>LayoutIdentifier</c> is Automation's own
/// context-local reference to a LayoutComposition layout, shaped like
/// <c>OverlayIdentifier</c> — no <c>IComparable</c>, nothing orders layouts
/// here (mirrors <c>OverlayIdentifierTests</c>). A distinct type from
/// <c>LayoutComposition.Domain.Layout.LayoutIdentifier</c> (§III).
/// </summary>
public class LayoutIdentifierTests
{
    [Fact]
    public void Wraps_a_guid()
    {
        Guid value = Guid.CreateVersion7();
        LayoutIdentifier.From(value).Value.ShouldBe(value);
    }

    [Fact]
    public void Rejects_the_empty_guid()
    {
        Should.Throw<ArgumentException>(() => LayoutIdentifier.From(Guid.Empty));
    }

    [Fact]
    public void Two_references_to_the_same_layout_are_equal()
    {
        Guid value = Guid.CreateVersion7();
        LayoutIdentifier.From(value).ShouldBe(LayoutIdentifier.From(value));
    }

    [Fact]
    public void Renders_as_its_guid()
    {
        Guid value = Guid.CreateVersion7();
        LayoutIdentifier.From(value).ToString().ShouldBe(value.ToString());
    }
}
