using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class IngestedAtTests
{
    [Fact]
    public void From_normalises_to_UTC()
    {
        DateTimeOffset local = new(2026, 5, 28, 12, 0, 0, TimeSpan.FromHours(2));
        IngestedAt.From(local).Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Round_trips_an_already_UTC_moment()
    {
        DateTimeOffset utc =
            DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);
        IngestedAt.From(utc).Value.ShouldBe(utc);
    }

    [Fact]
    public void Implicitly_unwraps_to_its_DateTimeOffset()
    {
        DateTimeOffset utc = new(2026, 5, 28, 8, 14, 33, TimeSpan.Zero);
        DateTimeOffset unwrapped = IngestedAt.From(utc);
        unwrapped.ShouldBe(utc);
    }
}
