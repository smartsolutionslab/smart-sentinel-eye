using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// The two range theories carry every row and the expected exception type
/// across from <c>LabelTests</c> unchanged. After spec 060 <c>Label.From</c>
/// cannot be handed a bad coordinate — the factory that refuses one lives here
/// — so the behaviour asserted is identical and the case count does not fall.
/// </summary>
public class NormalizedPositionTests
{
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void From_rejects_normalizedX_outside_0_to_1(double value)
    {
        Should.Throw<ArgumentException>(() => NormalizedPosition.From((decimal)value, 0m));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void From_rejects_normalizedY_outside_0_to_1(double value)
    {
        Should.Throw<ArgumentException>(() => NormalizedPosition.From(0m, (decimal)value));
    }

    [Fact]
    public void From_accepts_both_bounds_of_the_unit_interval()
    {
        NormalizedPosition position = NormalizedPosition.From(0m, 1m);

        position.X.ShouldBe(0m);
        position.Y.ShouldBe(1m);
    }

    [Fact]
    public void From_keeps_the_two_coordinates_in_the_order_they_were_given()
    {
        NormalizedPosition position = NormalizedPosition.From(0.25m, 0.75m);

        position.X.ShouldBe(0.25m);
        position.Y.ShouldBe(0.75m);
    }

    [Fact]
    public void Two_positions_with_the_same_coordinates_are_equal()
    {
        NormalizedPosition a = NormalizedPosition.From(0.2m, 0.3m);
        NormalizedPosition b = NormalizedPosition.From(0.2m, 0.3m);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void A_transposed_position_is_not_equal_to_the_original()
    {
        NormalizedPosition.From(0.2m, 0.3m).ShouldNotBe(NormalizedPosition.From(0.3m, 0.2m));
    }

    /// <summary>
    /// Fractional on purpose. Decimal formatting follows the current culture, so
    /// a comma-decimal host renders this pair as <c>(0,5,0,75)</c> — separator
    /// and decimal point the same character — unless the override formats
    /// invariantly. Asserting whole numbers would pass either way and prove
    /// nothing about the case that goes wrong.
    /// </summary>
    [Fact]
    public void ToString_reads_as_a_coordinate_pair_in_any_culture()
    {
        NormalizedPosition.From(0.5m, 0.75m).ToString().ShouldBe("(0.5,0.75)");
    }
}
