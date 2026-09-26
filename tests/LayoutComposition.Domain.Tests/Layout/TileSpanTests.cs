using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

/// <summary>
/// Spec 258 (ADR-0156 §2): <c>TileSpan</c> is the value object a tile's
/// <c>RowSpan</c>/<c>ColSpan</c> ride on, mirroring <c>GridPosition</c>. Only
/// the lower bound (≥1) is guarded here — the upper bound is the grid's, so
/// it is validated by <see cref="GridDimensions"/> (plan §2.1).
/// </summary>
public class TileSpanTests
{
    [Fact]
    public void Single_is_a_1x1_span()
    {
        TileSpan.Single.Rows.ShouldBe(1);
        TileSpan.Single.Cols.ShouldBe(1);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(9, 1)]
    public void From_accepts_a_span_of_at_least_one_row_and_column(int rows, int cols)
    {
        TileSpan span = TileSpan.From(rows, cols);

        span.Rows.ShouldBe(rows);
        span.Cols.ShouldBe(cols);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void From_rejects_a_non_positive_row_or_column(int rows, int cols)
    {
        Action act = () => TileSpan.From(rows, cols);
        act.ShouldThrow<ArgumentException>();
    }
}
