using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// The optimistic-concurrency token.
///
/// <para>
/// The first test is the one that matters. EF Core derives the concurrency
/// value comparer from this type's equality and uses it to decide whether a
/// write is stale. If <c>AggregateVersion</c> stopped being a <c>record</c>, or
/// gained equality that compared anything other than the value, two versions
/// holding the same number would compare unequal — and every write would look
/// stale, or none would. Nothing would fail to compile, and no test that fakes
/// a repository would notice, because the comparer only runs against a real
/// database.
/// </para>
///
/// <para>
/// So the guarantee is asserted here directly rather than left to the class
/// comment claiming it.
/// </para>
/// </summary>
public class AggregateVersionTests
{
    [Fact]
    public void Two_versions_holding_the_same_number_are_equal()
    {
        AggregateVersion.From(7).ShouldBe(AggregateVersion.From(7));
        AggregateVersion.From(7).GetHashCode().ShouldBe(AggregateVersion.From(7).GetHashCode());
    }

    [Fact]
    public void Two_versions_holding_different_numbers_are_not_equal()
    {
        AggregateVersion.From(7).ShouldNotBe(AggregateVersion.From(8));
    }

    /// <summary>
    /// A fresh aggregate is at zero, and zero is a legal version rather than a
    /// sentinel — the guard rejects only negatives.
    /// </summary>
    [Fact]
    public void An_unwritten_aggregate_starts_at_zero()
    {
        AggregateVersion.Initial.Value.ShouldBe(0);
        AggregateVersion.From(0).ShouldBe(AggregateVersion.Initial);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_negative_version_is_refused(int value)
    {
        Action act = () => AggregateVersion.From(value);

        act.ShouldThrow<ArgumentException>();
    }

    /// <summary>
    /// The implicit unwrap is what let this change land as 28 compile errors
    /// rather than 92: most call sites already held an <c>int</c> and keep
    /// reading naturally.
    /// </summary>
    [Fact]
    public void A_version_unwraps_to_its_number()
    {
        int unwrapped = AggregateVersion.From(42);

        unwrapped.ShouldBe(42);
        AggregateVersion.From(42).ToString().ShouldBe("42");
    }
}
