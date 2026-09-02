using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class OverlayIdentifierTests
{
    [Fact]
    public void Wraps_a_guid()
    {
        Guid value = Guid.CreateVersion7();
        OverlayIdentifier.From(value).Value.ShouldBe(value);
    }

    [Fact]
    public void Rejects_the_empty_guid()
    {
        Should.Throw<ArgumentException>(() => OverlayIdentifier.From(Guid.Empty));
    }

    [Fact]
    public void Two_references_to_the_same_overlay_are_equal()
    {
        Guid value = Guid.CreateVersion7();
        OverlayIdentifier.From(value).ShouldBe(OverlayIdentifier.From(value));
    }

    [Fact]
    public void Renders_as_its_guid()
    {
        Guid value = Guid.CreateVersion7();
        OverlayIdentifier.From(value).ToString().ShouldBe(value.ToString());
    }
}
