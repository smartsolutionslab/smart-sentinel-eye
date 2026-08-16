using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// The grammar must match Identity's, EventIngestion's, Automation's,
/// SystemVariables' and CameraCatalog's copies exactly. A stream's fab is never
/// authored here — it arrives on <c>CameraRegisteredV1</c>, having already
/// passed CameraCatalog's copy — so a value this context rejects and that one
/// accepts would silently drop a well-formed camera's stream.
///
/// <para>
/// ADR-0044 keeps the six copies separate on purpose. Nothing keeps them in
/// step but tests like this one — which is the whole reason it exists rather
/// than being skipped as a duplicate of the other five.
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

    [Fact]
    public void Rejects_null()
    {
        Should.Throw<ArgumentException>(() => FabIdentifier.From(null));
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
}
