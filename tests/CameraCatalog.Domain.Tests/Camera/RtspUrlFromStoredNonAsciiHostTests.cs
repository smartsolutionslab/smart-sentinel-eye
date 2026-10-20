using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Characterisation (spec 421 / issue #3023, FR-003, ADR-0144), captured green on <c>cabe008a</c> and required to stay
/// green after: a row stored with a non-ASCII plain host is read back by <see cref="RtspUrl.FromStored"/> exactly as
/// stored. <see cref="RtspUrl.From"/> is about to refuse every one of these; the read path must not, or one such row
/// takes the fab's camera list down.
/// </summary>
public class RtspUrlFromStoredNonAsciiHostTests
{
    public static TheoryData<string, string> Stored
    {
        get
        {
            TheoryData<string, string> rows = [];
            foreach (var (label, address) in NonAsciiHostRows.NonAscii)
            {
                rows.Add(label, address);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Stored))]
    public void FromStored_reads_back_every_address_the_write_path_is_about_to_refuse(string label, string stored)
    {
        RtspUrl.FromStored(stored).Value.ShouldBe(stored, label);
    }

    [Fact]
    public void The_set_holds_exactly_the_rows_the_spec_lists()
    {
        Stored.Cast<object[]>().Count().ShouldBe(38);
    }
}
