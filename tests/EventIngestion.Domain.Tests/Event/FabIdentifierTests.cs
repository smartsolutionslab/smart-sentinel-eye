using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class FabIdentifierTests
{
    [Theory]
    [InlineData("munich")]
    [InlineData("munich-1")]
    [InlineData("fab1-eu")]
    [InlineData("ab")]
    public void Accepts_well_formed_kebab_lowercase_names(string raw)
    {
        var fab = FabIdentifier.From(raw);
        fab.Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]                          // too short
    [InlineData("Munich")]                     // uppercase
    [InlineData("1munich")]                    // starts with digit
    [InlineData("munich_1")]                   // underscore not allowed
    [InlineData("munich.1")]                   // dot not allowed
    [InlineData("munich 1")]                   // space
    public void Rejects_malformed_input(string raw)
    {
        Action act = () => FabIdentifier.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_overlong_name()
    {
        Action act = () => FabIdentifier.From(new string('a', FabIdentifier.MaximumLength + 1));
        act.ShouldThrow<ArgumentException>();
    }

    // ---- spec 039: the ordering (issue 1849) ----

    [Fact]
    public void Orders_two_fabs_ordinally()
    {
        var earlier = FabIdentifier.From("aachen");
        var later = FabIdentifier.From("munich");

        earlier.CompareTo(later).ShouldBeLessThan(0);
        later.CompareTo(earlier).ShouldBeGreaterThan(0);
        (earlier < later).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }

    /// <summary>
    /// Without this, an implementation that always returns a positive number
    /// passes every ordering assertion above.
    /// </summary>
    [Fact]
    public void Two_equal_fabs_compare_equal()
    {
        FabIdentifier.From("munich").CompareTo(FabIdentifier.From("munich")).ShouldBe(0);
        (FabIdentifier.From("munich") <= FabIdentifier.From("munich")).ShouldBeTrue();
    }

    /// <summary>
    /// The case implementers forget, and that no ordinary sort reaches — a
    /// present value sorts after an absent one, matching CameraName.
    /// </summary>
    [Fact]
    public void A_fab_sorts_after_nothing()
    {
        FabIdentifier.From("munich").CompareTo(null).ShouldBeGreaterThan(0);
    }
}
