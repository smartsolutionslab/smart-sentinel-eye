using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall;

/// <summary>
/// Spec 258 plan.md §2: <c>WallIdentifier</c> is a Guid v7
/// <c>…Identifier</c> record (ADR-0039/0090), the same shape as
/// <c>LayoutIdentifier</c> (mirrors <c>LayoutIdentifierTests</c>).
/// </summary>
public class WallIdentifierTests
{
    [Fact]
    public void New_returns_a_non_empty_sortable_Guid_v7()
    {
        WallIdentifier a = WallIdentifier.New();
        WallIdentifier b = WallIdentifier.New();

        a.Value.ShouldNotBe(Guid.Empty);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void From_rejects_an_empty_Guid()
    {
        Action act = () => WallIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_non_empty_Guid()
    {
        Guid value = Guid.CreateVersion7();
        WallIdentifier id = WallIdentifier.From(value);
        id.Value.ShouldBe(value);
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = WallIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }
}
