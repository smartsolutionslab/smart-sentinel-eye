using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

/// <summary>
/// The grammar must match Identity's, EventIngestion's, Automation's,
/// SystemVariables', CameraCatalog's and StreamDistribution's copies exactly.
///
/// <para>
/// This context is where a divergence would hurt most. One fab string has to
/// mean the same thing in three places at once here: the caller's Keycloak
/// group, the camera's fab held by CameraCatalog (FR-014), and the SignalR
/// group name a kiosk joined on connect. A value one accepted and another
/// rejected would deliver a frame to nobody — and a frame that never arrives
/// looks exactly like a frame that was correctly withheld.
/// </para>
///
/// <para>
/// ADR-0044 keeps the seven copies separate on purpose. Nothing keeps them in
/// step but tests like this one — which is the whole reason it exists rather
/// than being skipped as a duplicate of the other six.
/// </para>
/// </summary>
public class FabIdentifierTests
{
    [Theory]
    [InlineData("munich")]
    [InlineData("dresden")]
    [InlineData("fab-2")]
    [InlineData("a1")]
    public void Accepts_lowercase_names_starting_with_a_letter(string value)
    {
        FabIdentifier.From(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_value(string value)
    {
        Should.Throw<ArgumentException>(() => FabIdentifier.From(value));
    }

    /// <summary>
    /// <c>null!</c> rather than plain <c>null</c>, unlike the sibling copies'
    /// tests: NRT is <em>enabled</em> in this test project, so the
    /// null-forgiving operator is required to compile and S8970 — which fires
    /// on <c>!</c> where NRT is disabled — does not apply. Same assertion,
    /// opposite spelling, and the reason is the project setting rather than
    /// the code under test.
    /// </summary>
    [Fact]
    public void Rejects_null()
    {
        Should.Throw<ArgumentException>(() => FabIdentifier.From(null!));
    }

    [Theory]
    [InlineData("m")]                                   // below the minimum
    [InlineData("Munich")]                              // uppercase
    [InlineData("2fab")]                                // starts with a digit
    [InlineData("-fab")]                                // starts with a hyphen
    [InlineData("fab_2")]                               // underscore
    [InlineData("fab 2")]                               // space
    [InlineData("münchen")]                             // non-ASCII
    public void Rejects_values_outside_the_grammar(string value)
    {
        Should.Throw<ArgumentException>(() => FabIdentifier.From(value));
    }

    [Fact]
    public void Rejects_a_value_past_the_maximum_length()
    {
        string tooLong = "f" + new string('a', FabIdentifier.MaximumLength);

        Should.Throw<ArgumentException>(() => FabIdentifier.From(tooLong));
    }

    [Fact]
    public void Accepts_a_value_at_exactly_the_maximum_length()
    {
        string atLimit = "f" + new string('a', FabIdentifier.MaximumLength - 1);

        FabIdentifier.From(atLimit).Value.Length.ShouldBe(FabIdentifier.MaximumLength);
    }

    [Fact]
    public void Two_identifiers_with_the_same_value_are_equal()
    {
        FabIdentifier.From("munich").ShouldBe(FabIdentifier.From("munich"));
    }

    [Fact]
    public void Identifiers_for_different_fabs_are_not_equal()
    {
        FabIdentifier.From("munich").ShouldNotBe(FabIdentifier.From("dresden"));
    }

    // ---- spec 039: the ordering (issue 1849) ----

    [Fact]
    public void Orders_two_fabs_ordinally()
    {
        FabIdentifier earlier = FabIdentifier.From("aachen");
        FabIdentifier later = FabIdentifier.From("munich");

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
