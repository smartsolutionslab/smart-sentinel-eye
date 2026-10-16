using System.Globalization;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 385. The ordering contract on the instant a stream's address was last
/// changed: the repoint guard compares these, so a missing or wrong operator is
/// a stale address winning.
/// </summary>
public class SourceChangedAtTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddMinutes(1);

    [Fact]
    public void An_earlier_instant_orders_before_a_later_one()
    {
        var earlier = SourceChangedAt.From(Earlier);
        var later = SourceChangedAt.From(Later);

        earlier.CompareTo(later).ShouldBeLessThan(0);
        later.CompareTo(earlier).ShouldBeGreaterThan(0);
        earlier.CompareTo(SourceChangedAt.From(Earlier)).ShouldBe(0);
    }

    [Fact]
    public void Anything_orders_after_nothing()
    {
        SourceChangedAt.From(Earlier).CompareTo(null).ShouldBe(1);
    }

    [Fact]
    public void The_operators_agree_with_CompareTo()
    {
        var earlier = SourceChangedAt.From(Earlier);
        var later = SourceChangedAt.From(Later);
        var equal = SourceChangedAt.From(Earlier);

        (earlier < later).ShouldBeTrue();
        (later < earlier).ShouldBeFalse();
        (earlier < equal).ShouldBeFalse();
        (later > earlier).ShouldBeTrue();
        (earlier > later).ShouldBeFalse();
        (earlier > equal).ShouldBeFalse();
        (earlier <= later).ShouldBeTrue();
        (earlier <= equal).ShouldBeTrue();
        (later <= earlier).ShouldBeFalse();
        (later >= earlier).ShouldBeTrue();
        (earlier >= equal).ShouldBeTrue();
        (earlier >= later).ShouldBeFalse();
    }

    [Fact]
    public void Instants_sort_in_memory()
    {
        SourceChangedAt[] sorted =
            [.. new[] { SourceChangedAt.From(Later), SourceChangedAt.From(Earlier) }.OrderBy(instant => instant)];

        sorted[0].ShouldBe(SourceChangedAt.From(Earlier));
    }

    [Fact]
    public void Construction_normalises_to_UTC_without_moving_the_instant()
    {
        DateTimeOffset elsewhere = new(2026, 10, 14, 11, 0, 0, TimeSpan.FromHours(2));

        var instant = SourceChangedAt.From(elsewhere);

        instant.Value.Offset.ShouldBe(TimeSpan.Zero);
        DateTimeOffset unwrapped = instant;
        unwrapped.ShouldBe(elsewhere);
        instant.ShouldBe(SourceChangedAt.From(Earlier));
        instant.ToString().ShouldBe(Earlier.ToString("O", CultureInfo.InvariantCulture));
    }
}
