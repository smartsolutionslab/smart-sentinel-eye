using System.Net;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

/// <summary>
/// ADR-0047 gives every command its own error union, so the stale-version
/// case is declared five times. ADR-0104 accepts that duplication but asks
/// for it to be kept in sync by hand — this is the guard that catches it
/// drifting.
/// </summary>
public class LayoutRevisionStaleErrorTests
{
    private const string ExpectedCode = "LAYOUT_REVISION_STALE";

    private static readonly Guid Layout = Guid.CreateVersion7();

    private static ApiError[] EveryStaleCase() =>
    [
        new PublishRevisionError.LayoutRevisionStale(Layout, 3, 4),
        new ArchiveRevisionError.LayoutRevisionStale(Layout, 3, 4),
        new BranchDraftRevisionError.LayoutRevisionStale(Layout, 3, 4),
        new EditDraftRevisionError.LayoutRevisionStale(Layout, 3, 4),
        new RevertRevisionError.LayoutRevisionStale(Layout, 3, 4),
    ];

    [Fact]
    public void Every_mutating_command_has_a_stale_case()
    {
        EveryStaleCase().Length.ShouldBe(5);
    }

    [Fact]
    public void Every_stale_case_reports_the_same_code()
    {
        EveryStaleCase().ShouldAllBe(error => error.Code == ExpectedCode);
    }

    // 409 rather than 412: the caller can act on it, and it matches the
    // Conflict cases already in these unions (ADR-0113).
    [Fact]
    public void Every_stale_case_maps_to_409_conflict()
    {
        EveryStaleCase().ShouldAllBe(error => error.Status == HttpStatusCode.Conflict);
    }

    [Fact]
    public void The_message_names_both_the_version_held_and_the_version_stored()
    {
        foreach (ApiError error in EveryStaleCase())
        {
            error.Message.ShouldContain("3");
            error.Message.ShouldContain("4");
        }
    }
}
