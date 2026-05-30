using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class OccurredAtTests
{
    [Fact]
    public void From_normalises_a_local_time_to_UTC()
    {
        DateTimeOffset local = new(2026, 5, 28, 12, 0, 0, TimeSpan.FromHours(2));
        OccurredAt occurred = OccurredAt.From(local);
        occurred.Value.Offset.ShouldBe(TimeSpan.Zero);
        occurred.Value.UtcDateTime.ShouldBe(local.UtcDateTime);
    }

    [Fact]
    public void Round_trips_an_already_UTC_moment()
    {
        DateTimeOffset utc =
            DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);
        OccurredAt.From(utc).Value.ShouldBe(utc);
    }

    [Fact]
    public void Implicitly_unwraps_to_its_DateTimeOffset()
    {
        DateTimeOffset utc = new(2026, 5, 28, 8, 14, 33, TimeSpan.Zero);
        DateTimeOffset unwrapped = OccurredAt.From(utc);
        unwrapped.ShouldBe(utc);
    }
}
