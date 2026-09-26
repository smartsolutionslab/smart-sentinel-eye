using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall;

/// <summary>
/// Spec 258 plan.md §2: <c>SceneVersion</c> is a monotonic long VO, starting
/// at <c>Initial</c> (0) and advancing by exactly one per applied switch
/// (FR-004). Lets the kiosk discard out-of-order <c>WallSceneChanged</c>
/// frames, the same device as <c>ResolvedOverlayTextChangedNotification</c>'s
/// per-overlay <c>Version</c>.
/// </summary>
public class SceneVersionTests
{
    [Fact]
    public void Initial_is_zero()
    {
        SceneVersion.Initial.Value.ShouldBe(0L);
    }

    [Fact]
    public void Next_returns_the_successor()
    {
        SceneVersion.Initial.Next().Value.ShouldBe(1L);
    }

    [Fact]
    public void Next_advances_by_exactly_one_each_call()
    {
        SceneVersion version = SceneVersion.Initial.Next().Next().Next();
        version.Value.ShouldBe(3L);
    }

    [Fact]
    public void From_round_trips_a_non_negative_value()
    {
        SceneVersion.From(7).Value.ShouldBe(7L);
    }

    [Fact]
    public void From_rejects_a_negative_value()
    {
        Action act = () => SceneVersion.From(-1);
        act.ShouldThrow<ArgumentException>();
    }
}
