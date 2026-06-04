using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class GridPositionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(3, 2)]
    public void From_accepts_any_non_negative_coordinate(int row, int col)
    {
        GridPosition position = GridPosition.From(row, col);
        position.Row.ShouldBe(row);
        position.Col.ShouldBe(col);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void From_rejects_a_negative_coordinate(int row, int col)
    {
        Action act = () => GridPosition.From(row, col);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Positions_with_the_same_coordinates_are_equal()
    {
        GridPosition.From(1, 2).ShouldBe(GridPosition.From(1, 2));
    }
}
