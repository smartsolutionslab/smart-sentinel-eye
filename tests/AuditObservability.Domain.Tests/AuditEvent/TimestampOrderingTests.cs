using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

/// <summary>
/// The ordering contract on this context's instants.
///
/// <para>
/// Not incidental coverage. Every one of these types was written without
/// <c>IComparable</c>, and <c>Comparer&lt;T&gt;.Default</c> threw "At least one
/// object must implement IComparable" the moment a list of them was sorted in
/// memory — twenty-two tests, across six projects. Real EF hides the gap by
/// translating <c>OrderBy</c> into SQL, so only a fake ever sees it.
/// </para>
///
/// <para>
/// The comparison operators exist because <c>CA1036</c> requires them of an
/// <c>IComparable</c>, so they are exercised here rather than left as four
/// unread methods per type.
/// </para>
/// </summary>
public class TimestampOrderingTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddHours(1);

    private static void OrdersByItsMoment<T>(Func<DateTimeOffset, T> from)
        where T : class, IComparable<T>
    {
        T earlier = from(Earlier);
        T later = from(Later);

        earlier.CompareTo(later).ShouldBeLessThan(0);
        later.CompareTo(earlier).ShouldBeGreaterThan(0);
        earlier.CompareTo(from(Earlier)).ShouldBe(0);
        earlier.CompareTo(null).ShouldBeGreaterThan(0);

        // The exact call that failed before IComparable existed.
        T[] sorted = [.. new[] { later, earlier }.OrderBy(instant => instant)];
        sorted[0].ShouldBe(earlier);
    }

    private static void OperatorsAgreeWithCompareTo<T>(
        Func<DateTimeOffset, T> from,
        Func<T, T, bool> lessThan,
        Func<T, T, bool> greaterThan,
        Func<T, T, bool> lessOrEqual,
        Func<T, T, bool> greaterOrEqual)
    {
        T earlier = from(Earlier);
        T later = from(Later);

        lessThan(earlier, later).ShouldBeTrue();
        lessThan(later, earlier).ShouldBeFalse();
        greaterThan(later, earlier).ShouldBeTrue();
        greaterThan(earlier, later).ShouldBeFalse();
        lessOrEqual(earlier, from(Earlier)).ShouldBeTrue();
        greaterOrEqual(later, from(Later)).ShouldBeTrue();
    }

    /// <summary>
    /// Construction normalizes to UTC without moving the instant, and
    /// <c>ToString</c> renders round-trip "O".
    /// </summary>
    private static void NormalizesWithoutMovingTheInstant<T>(
        Func<DateTimeOffset, T> from, Func<T, DateTimeOffset> unwrap)
    {
        DateTimeOffset elsewhere = new(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(2));
        T instant = from(elsewhere);

        unwrap(instant).Offset.ShouldBe(TimeSpan.Zero);
        unwrap(instant).ShouldBe(elsewhere);
        instant!.ToString()!.ShouldBe(elsewhere.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Every_instant_orders_normalizes_and_compares()
    {
        OrdersByItsMoment(OccurredAt.From);
        OrdersByItsMoment(ReceivedAt.From);
        OrdersByItsMoment(HandlerEnteredAt.From);
        OrdersByItsMoment(WrittenAt.From);

        OperatorsAgreeWithCompareTo(OccurredAt.From, (a, b) => a < b, (a, b) => a > b, (a, b) => a <= b, (a, b) => a >= b);
        OperatorsAgreeWithCompareTo(ReceivedAt.From, (a, b) => a < b, (a, b) => a > b, (a, b) => a <= b, (a, b) => a >= b);
        OperatorsAgreeWithCompareTo(HandlerEnteredAt.From, (a, b) => a < b, (a, b) => a > b, (a, b) => a <= b, (a, b) => a >= b);
        OperatorsAgreeWithCompareTo(WrittenAt.From, (a, b) => a < b, (a, b) => a > b, (a, b) => a <= b, (a, b) => a >= b);

        NormalizesWithoutMovingTheInstant(OccurredAt.From, x => x);
        NormalizesWithoutMovingTheInstant(ReceivedAt.From, x => x);
        NormalizesWithoutMovingTheInstant(HandlerEnteredAt.From, x => x);
        NormalizesWithoutMovingTheInstant(WrittenAt.From, x => x);
    }
}
