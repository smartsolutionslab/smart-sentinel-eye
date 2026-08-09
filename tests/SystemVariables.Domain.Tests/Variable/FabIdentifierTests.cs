using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

/// <summary>
/// The grammar must match Identity's, EventIngestion's and Automation's copies
/// exactly. The same fab string reaches this context on a value-change raised
/// by a rule and from a caller's Keycloak group, so a value one context accepts
/// and another rejects would strand variables that can never resolve (ADR-0044
/// keeps the copies separate; nothing keeps them in step but tests like this).
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
}
