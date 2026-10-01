using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

/// <summary>
/// Spec 296 plan.md §2.1: <c>WallIdentifier</c> is Automation's own
/// context-local reference to a LayoutComposition wall, shaped like
/// <c>OverlayIdentifier</c> — no <c>IComparable</c>, nothing orders walls
/// here (mirrors <c>OverlayIdentifierTests</c>).
/// </summary>
public class WallIdentifierTests
{
    [Fact]
    public void Wraps_a_guid()
    {
        Guid value = Guid.CreateVersion7();
        WallIdentifier.From(value).Value.ShouldBe(value);
    }

    [Fact]
    public void Rejects_the_empty_guid()
    {
        Should.Throw<ArgumentException>(() => WallIdentifier.From(Guid.Empty));
    }

    [Fact]
    public void Two_references_to_the_same_wall_are_equal()
    {
        Guid value = Guid.CreateVersion7();
        WallIdentifier.From(value).ShouldBe(WallIdentifier.From(value));
    }

    [Fact]
    public void Renders_as_its_guid()
    {
        Guid value = Guid.CreateVersion7();
        WallIdentifier.From(value).ToString().ShouldBe(value.ToString());
    }
}
