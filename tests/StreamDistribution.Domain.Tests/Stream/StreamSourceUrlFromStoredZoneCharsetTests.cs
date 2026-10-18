using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Characterisation (spec 411 / issue #3023, FR-004), captured green on <c>e5be6d72</c> and required to stay green
/// after: a row stored with a non-printable zone or a punctuation-only host is read back by
/// <see cref="StreamSourceUrl.FromStored"/> exactly as stored. <see cref="StreamSourceUrl.From"/> is about to refuse every one of
/// these; the read path must not, or one such row takes the fab's camera list down.
/// </summary>
public class StreamSourceUrlFromStoredZoneCharsetTests
{
    public static TheoryData<string> Stored => [.. ZoneCharsetRows.EveryRefusedAddress()];

    [Theory]
    [MemberData(nameof(Stored))]
    public void FromStored_reads_back_every_address_the_write_path_is_about_to_refuse(string stored)
    {
        StreamSourceUrl.FromStored(stored).Value.ShouldBe(stored);
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        Stored.Cast<object[]>().Count().ShouldBe(7 + 15 + 11 + 10);
    }
}
