using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class GridDimensionsTests
{
    [Fact]
    public void MaxTiles_and_MaxCells_are_four()
    {
        GridDimensions.MaxTiles.ShouldBe(4);
        GridDimensions.MaxCells.ShouldBe(4);
    }

    [Fact]
    public void Default_is_a_2x2_grid()
    {
        GridDimensions.Default.Rows.ShouldBe(2);
        GridDimensions.Default.Cols.ShouldBe(2);
    }

    [Fact]
    public void Cell_is_a_1x1_grid()
    {
        GridDimensions.Cell.Rows.ShouldBe(1);
        GridDimensions.Cell.Cols.ShouldBe(1);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void From_accepts_grids_within_the_cell_cap(int rows, int cols)
    {
        GridDimensions grid = GridDimensions.From(rows, cols);
        grid.Rows.ShouldBe(rows);
        grid.Cols.ShouldBe(cols);
    }

    [Theory]
    [InlineData(0, 1)]   // non-positive rows
    [InlineData(1, 0)]   // non-positive cols
    [InlineData(-1, 1)]  // negative rows
    [InlineData(2, 3)]   // exceeds the cell cap
    [InlineData(3, 2)]   // exceeds the cell cap
    [InlineData(5, 1)]   // exceeds the cell cap
    public void From_rejects_an_invalid_grid(int rows, int cols)
    {
        Action act = () => GridDimensions.From(rows, cols);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Contains_is_true_for_an_in_bounds_position()
    {
        GridDimensions.Default.Contains(GridPosition.From(1, 1)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void Contains_is_false_for_an_out_of_bounds_position(int row, int col)
    {
        GridDimensions.Default.Contains(GridPosition.From(row, col)).ShouldBeFalse();
    }
}
