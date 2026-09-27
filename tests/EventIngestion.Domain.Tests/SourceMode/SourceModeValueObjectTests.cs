using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using Declaration = SmartSentinelEye.EventIngestion.Domain.SourceMode.Declaration;
using DeclaredAt = SmartSentinelEye.EventIngestion.Domain.SourceMode.DeclaredAt;
using EventTypeMode = SmartSentinelEye.EventIngestion.Domain.SourceMode.EventTypeMode;
using SourceModeIdentifier = SmartSentinelEye.EventIngestion.Domain.SourceMode.SourceModeIdentifier;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.SourceMode;

/// <summary>
/// Phase 4a (spec 269 T003b). The four value objects.
///
/// <para>
/// Not every test here is red on arrival. T001 marks
/// <c>SourceModeIdentifier</c> "fully implemented — copy
/// RegisteredEventTypeIdentifier; it has no behaviour to withhold", and
/// <c>Declaration.From</c> is "the guards and the construction, nothing
/// else" — there is no behaviour to withhold, so both are already correct,
/// the same category tasks.md's T003a.7 names for the aggregate's null
/// guards. The first row of
/// <see cref="EventTypeMode_From_round_trips_strict_and_discovery"/>
/// (<c>"discovery"</c>) also passes on arrival, as a direct, unavoidable
/// consequence of T001's <c>From</c> always answering <c>Discovery</c> — not
/// because the switch exists yet. See the phase 4a report for the full,
/// verified list — tasks.md's own green-on-arrival list (T003a.7, T003e.13)
/// does not enumerate these.
/// </para>
/// </summary>
public class SourceModeValueObjectTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("strict")]
    [InlineData("discovery")]
    public void EventTypeMode_From_round_trips_strict_and_discovery(string wire)
    {
        EventTypeMode.From(wire).Value.ShouldBe(wire);
    }

    [Fact]
    public void EventTypeMode_From_refuses_an_unknown_mode()
    {
        Should.Throw<ArgumentException>(() => EventTypeMode.From("paranoid"));
    }

    /// <summary>
    /// Decision 018's own words and <c>Source</c>'s wire convention are
    /// lowercase; a PascalCase spelling must not be accepted as a synonym.
    /// </summary>
    [Fact]
    public void EventTypeMode_From_is_case_sensitive()
    {
        Should.Throw<ArgumentException>(() => EventTypeMode.From("Strict"));
    }

    [Fact]
    public void DeclaredAt_stores_the_instant_in_UTC()
    {
        DateTimeOffset nonUtc = new(2026, 5, 28, 10, 14, 33, TimeSpan.FromHours(2));

        DeclaredAt at = DeclaredAt.From(nonUtc);

        at.Value.Offset.ShouldBe(TimeSpan.Zero, "a non-UTC instant must come back UTC");
        at.Value.ShouldBe(nonUtc.ToUniversalTime());
    }

    /// <summary>
    /// Green on arrival: <c>CompareTo</c> and the comparison operators compare
    /// <c>DateTimeOffset</c> instants, which is offset-agnostic in .NET — this
    /// was never part of the UTC-normalisation behaviour T001 withholds.
    /// </summary>
    [Fact]
    public void DeclaredAt_orders_by_instant()
    {
        DeclaredAt earlier = DeclaredAt.From(Now);
        DeclaredAt later = DeclaredAt.From(Now.AddMinutes(1));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }

    /// <summary>
    /// Green on arrival: <c>Declaration.From</c> is "the guards and the
    /// construction, nothing else" per T001 — there is no behaviour to
    /// withhold, so this guard is already correct.
    /// </summary>
    [Fact]
    public void Declaration_refuses_a_null_declaredAt()
    {
        Should.Throw<ArgumentNullException>(() => Declaration.From(
            null!, OperatorIdentifier.From(Guid.CreateVersion7())));
    }

    /// <summary>
    /// Green on arrival: T001 marks the identifier "fully implemented — copy
    /// RegisteredEventTypeIdentifier".
    /// </summary>
    [Fact]
    public void SourceModeIdentifier_New_is_a_version_7_guid()
    {
        SourceModeIdentifier id = SourceModeIdentifier.New();

        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }
}
