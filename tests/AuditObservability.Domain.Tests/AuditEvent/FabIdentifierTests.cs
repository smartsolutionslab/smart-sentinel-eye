using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

/// <summary>
/// Spec 039 (issue 1849). This file did not exist: AuditObservability was the
/// one context of eight with no <c>FabIdentifierTests</c>, and the one whose
/// <c>FabIdentifier</c> body had already drifted from the other seven. Two
/// independent signs that this copy was added slightly apart from the rest.
///
/// <para>
/// Mirrors a sibling's structure rather than inventing one, because the eight
/// copies are meant to be the same and their tests should read the same too.
/// </para>
/// </summary>
public class FabIdentifierTests
{
    [Theory]
    [InlineData("munich")]
    [InlineData("munich-1")]
    [InlineData("ab")]
    public void Accepts_well_formed_kebab_lowercase_names(string raw) =>
        FabIdentifier.From(raw).Value.ShouldBe(raw);

    [Theory]
    [InlineData("")]
    [InlineData("a")]                          // too short
    [InlineData("Munich")]                     // uppercase
    [InlineData("1munich")]                    // starts with digit
    [InlineData("munich_1")]                   // underscore
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
