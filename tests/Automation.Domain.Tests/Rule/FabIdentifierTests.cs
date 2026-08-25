using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

/// <summary>
/// The grammar must match Identity's and EventIngestion's copies exactly.
/// The same fab string reaches this context from an ingested event and from a
/// caller's Keycloak group, so a value one context accepts and another
/// rejects would strand rules that can never be matched (ADR-0044 keeps the
/// copies separate; nothing keeps them in step but tests like this).
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

/// <summary>
/// A rule's fab is fixed at creation. Publishing or archiving must not
/// disturb it — if it could, a rule would change which plant's events it
/// acts on partway through its life, which is the defect spec 013 removes
/// rather than one it may reintroduce.
/// </summary>
public class RuleFabLifetimeTests
{
    [Fact]
    public void The_fab_survives_publish_and_archive()
    {
        Fakes.FakeClock clock = new(DateTimeOffset.UnixEpoch);
        SmartSentinelEye.Automation.Domain.Rule.Rule rule =
            new RuleBuilder().WithFab("dresden").Build();

        rule.Fab.Value.ShouldBe("dresden");

        rule.Publish(clock);
        rule.Fab.Value.ShouldBe("dresden");

        rule.Archive(clock);
        rule.Fab.Value.ShouldBe("dresden");
    }

    [Fact]
    public void The_aggregate_exposes_no_way_to_move_a_rule_between_fabs()
    {
        // Structural: relocation is out of scope by design (spec 013
        // Assumptions), and a public setter appearing later would be a silent
        // widening of what a rule can do.
        System.Reflection.PropertyInfo fab =
            typeof(SmartSentinelEye.Automation.Domain.Rule.Rule).GetProperty("Fab")!;

        fab.SetMethod?.IsPublic.ShouldNotBe(true);
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
