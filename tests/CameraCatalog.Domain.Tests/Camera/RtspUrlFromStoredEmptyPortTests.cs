using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Characterisation (spec 414 / issue #3031 item 3, FR-003), captured green on <c>1cbae37d</c> and required to stay
/// green after: a row stored with a ':' and no port is read back by <see cref="RtspUrl.FromStored"/> exactly as
/// stored. <see cref="RtspUrl.From"/> is about to refuse every one of these; the read path must not, or one such row
/// takes the fab's camera list down.
/// </summary>
public class RtspUrlFromStoredEmptyPortTests
{
    public static TheoryData<string> Stored => [.. EmptyPortRows.EmptyPort];

    [Theory]
    [MemberData(nameof(Stored))]
    public void FromStored_reads_back_every_address_the_write_path_is_about_to_refuse(string stored)
    {
        RtspUrl.FromStored(stored).Value.ShouldBe(stored);
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        Stored.Cast<object[]>().Count().ShouldBe(24);
    }
}
