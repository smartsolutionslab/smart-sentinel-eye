using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// The two range theories carry every row — including the <c>0</c> row that
/// pins a zero extent as refused — and the expected exception type across from
/// <c>LabelTests</c> unchanged.
/// </summary>
public class NormalizedSizeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void From_rejects_normalizedWidth_outside_0_exclusive_to_1(double value)
    {
        Should.Throw<ArgumentException>(() => NormalizedSize.From((decimal)value, 0.1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void From_rejects_normalizedHeight_outside_0_exclusive_to_1(double value)
    {
        Should.Throw<ArgumentException>(() => NormalizedSize.From(0.1m, (decimal)value));
    }

    [Fact]
    public void From_accepts_a_full_cell()
    {
        NormalizedSize size = NormalizedSize.From(1m, 1m);

        size.Width.ShouldBe(1m);
        size.Height.ShouldBe(1m);
    }

    [Fact]
    public void From_keeps_the_two_extents_in_the_order_they_were_given()
    {
        NormalizedSize size = NormalizedSize.From(0.3m, 0.08m);

        size.Width.ShouldBe(0.3m);
        size.Height.ShouldBe(0.08m);
    }

    [Fact]
    public void Two_sizes_with_the_same_extents_are_equal()
    {
        NormalizedSize a = NormalizedSize.From(0.4m, 0.5m);
        NormalizedSize b = NormalizedSize.From(0.4m, 0.5m);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void A_transposed_size_is_not_equal_to_the_original()
    {
        NormalizedSize.From(0.4m, 0.5m).ShouldNotBe(NormalizedSize.From(0.5m, 0.4m));
    }

    /// <summary>
    /// Fractional on purpose, for the reason given on
    /// <c>NormalizedPositionTests.ToString_reads_as_a_coordinate_pair_in_any_culture</c>.
    /// </summary>
    [Fact]
    public void ToString_reads_as_width_by_height_in_any_culture()
    {
        NormalizedSize.From(0.5m, 0.75m).ToString().ShouldBe("0.5x0.75");
    }
}
